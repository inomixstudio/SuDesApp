using Google.Apis.Forms.v1;
using Google.Apis.Forms.v1.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Utilities
{
    public sealed class InfoFormulir
    {
        public string FormId { get; }
        public string ResponderUri { get; }
        public string EditUri { get; }

        public InfoFormulir(string formId, string responderUri, string editUri)
        {
            FormId = formId;
            ResponderUri = responderUri;
            EditUri = editUri;
        }
    }

    public class GoogleFormsService
    {
        private readonly GoogleDriveService _drive;
        private readonly ILogger<GoogleFormsService> _logger;
        private readonly SemaphoreSlim _clientLock = new(1, 1);
        private FormsService? _service;

        public GoogleFormsService(GoogleDriveService drive, ILogger<GoogleFormsService> logger)
        {
            _drive = drive ?? throw new ArgumentNullException(nameof(drive));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _drive.ClientInvalidated += ResetClient;
        }

        private async Task<FormsService> GetClientAsync(CancellationToken ct)
        {
            if (_service != null) return _service;

            await _clientLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_service != null) return _service;
                var initializer = await _drive.BuildSharedInitializerAsync(ct).ConfigureAwait(false);
                _service = new FormsService(initializer);
                return _service;
            }
            finally
            {
                _clientLock.Release();
            }
        }

        public static string FormsApiHint =>
            "Google Forms API belum aktif di proyek Google Cloud yang dipakai aplikasi.\n\n" +
            "Cara mengaktifkan (sekali saja, gratis):\n" +
            "1. Buka https://console.cloud.google.com/apis/library\n" +
            "2. Pilih proyek yang dipakai Client ID aplikasi.\n" +
            "3. Cari \"Google Forms API\" lalu klik Enable.\n\n" +
            "Detail teknis: {0}";

        /// <summary>
        /// Membuat formulir permohonan surat lengkap: satu item pertanyaan untuk
        /// setiap kolom katalog. Mengembalikan ID, URL isian warga, dan URL editor.
        /// </summary>
        public async Task<InfoFormulir> BuatFormulirAsync(
            string judul,
            string deskripsi,
            IReadOnlyList<WaFormPertanyaan> pertanyaan,
            CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);

            var form = await client.Forms.Create(new Form
            {
                Info = new Info { Title = judul, DocumentTitle = judul }
            }).ExecuteAsync(ct).ConfigureAwait(false);

            var formId = form.FormId;
            if (string.IsNullOrWhiteSpace(formId))
                throw new InvalidOperationException("Google Forms tidak mengembalikan ID formulir.");

            var infoBatch = new BatchUpdateFormRequest
            {
                Requests = new List<Request>
                {
                    new Request
                    {
                        UpdateFormInfo = new UpdateFormInfoRequest
                        {
                            Info = new Info { Description = deskripsi },
                            UpdateMask = "description"
                        }
                    }
                }
            };
            await client.Forms.BatchUpdate(infoBatch, formId).ExecuteAsync(ct).ConfigureAwait(false);

            var requests = new List<Request>();
            for (int i = 0; i < pertanyaan.Count; i++)
            {
                var p = pertanyaan[i];
                requests.Add(new Request
                {
                    CreateItem = new CreateItemRequest
                    {
                        Item = new Item
                        {
                            Title = p.Judul,
                            Description = p.Bantuan,
                            QuestionItem = new QuestionItem { Question = BangunQuestion(p) }
                        },
                        Location = new Location { Index = i }
                    }
                });
            }

            if (requests.Count > 0)
            {
                await client.Forms.BatchUpdate(new BatchUpdateFormRequest { Requests = requests }, formId)
                    .ExecuteAsync(ct).ConfigureAwait(false);
            }

            var fresh = await client.Forms.Get(formId).ExecuteAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Formulir Google dibuat: {FormId} ({Jumlah} pertanyaan)", formId, pertanyaan.Count);

            return new InfoFormulir(
                formId,
                fresh.ResponderUri ?? string.Empty,
                $"https://docs.google.com/forms/d/{formId}/edit");
        }

        private static Question BangunQuestion(WaFormPertanyaan p)
        {
            var q = new Question { Required = p.Wajib };
            switch (p.Tipe)
            {
                case WaFormTipe.Paragraf:
                    q.TextQuestion = new TextQuestion { Paragraph = true };
                    break;
                case WaFormTipe.TeksPendek:
                    q.TextQuestion = new TextQuestion { Paragraph = false };
                    break;
                case WaFormTipe.Pilihan:
                    q.ChoiceQuestion = new ChoiceQuestion
                    {
                        Type = "RADIO",
                        Options = p.Pilihan.Select(o => new Option { Value = o }).ToList()
                    };
                    break;
            }
            return q;
        }

        /// <summary>Mengambil definisi formulir (dipakai memetakan questionId → judul kolom).</summary>
        public async Task<Form> GetFormAsync(string formId, CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            return await client.Forms.Get(formId).ExecuteAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Mengambil seluruh jawaban yang masuk (urut terbaru dahulu dari API).</summary>
        public async Task<IList<FormResponse>> ListResponsesAsync(string formId, CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var response = await client.Forms.Responses.List(formId).ExecuteAsync(ct).ConfigureAwait(false);
            return response.Responses ?? new List<FormResponse>();
        }

        /// <summary>Menggabungkan jawaban (pilihan/teks) menjadi satu teks kolom.</summary>
        public static string JawabanTeks(Answer? answer)
        {
            var daftar = answer?.TextAnswers?.Answers;
            if (daftar == null || daftar.Count == 0) return string.Empty;
            return string.Join(", ", daftar
                .Select(a => a.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        public void ResetClient()
        {
            var old = _service;
            _service = null;
            old?.Dispose();
        }
    }
}
