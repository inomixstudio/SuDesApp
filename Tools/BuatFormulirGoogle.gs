/**
 * ============================================================
 *  SuDesApp - Pembuat Google Formulir Layanan Surat (otomatis)
 * ============================================================
 *  CARA PAKAI (cukup sekali):
 *   1) Buka https://script.google.com  ->  "New project".
 *   2) Hapus isi editor, lalu tempel SELURUH isi berkas ini.
 *   3) Klik "Run" (tombol play), pilih fungsi "buatFormulirLayananSurat".
 *   4) Saat diminta izin, klik "Review permissions" -> pilih akun -> Allow.
 *   5) Buka "Execution log" (Ctrl+Enter). Di sana muncul 3 nilai yang
 *      tinggal disalin ke halaman Pengaturan Aplikasi:
 *        - URL Google Formulir   -> kolom "URL Google Formulir"
 *        - URL Sheet jawaban     -> kolom "URL Sheet jawaban"
 *        - Nama tab jawaban      -> kolom "Nama tab jawaban"
 *
 *  CATATAN: menjalankan ulang akan MEMBUAT form & spreadsheet BARU.
 * ============================================================
 */

var JUDUL_FORM = 'Formulir Layanan Surat Online Desa';
var JUDUL_SPREADSHEET = 'Jawaban Formulir Layanan Surat Desa';

// Pilihan jenis surat - HARUS sama dengan yang dikenali aplikasi.
var DAFTAR_JENIS = [
  'SKTM',
  'SKD Umum',
  'Domisili Warga',
  'Pengantar SKCK',
  'SKU (Keterangan Usaha)',
  'Izin Orang Tua',
  'Surat Instansi'
];

function buatFormulirLayananSurat() {
  // 1) Spreadsheet tempat jawaban akan terkumpul
  var ss = SpreadsheetApp.create(JUDUL_SPREADSHEET);
  var ssId = ss.getId();

  // 2) Formulir
  var form = FormApp.create(JUDUL_FORM);
  form.setTitle(JUDUL_FORM)
      .setDescription('Pilih jenis surat, lalu isi data Anda dengan lengkap. ' +
                      'Kolom bertanda bintang wajib diisi. Isi hanya kolom yang ' +
                      'berhubungan dengan jenis surat yang Anda pilih.')
      .setCollectEmail(false)
      .setAllowResponseEdits(false)
      .setAcceptingResponses(true);

  // 3) Kolom "Jenis Surat" (wajib) - pilihan
  var qJenis = form.addMultipleChoiceItem()
      .setTitle('Jenis Surat')
      .setRequired(true);
  qJenis.setChoices(DAFTAR_JENIS.map(function (j) { return qJenis.createChoice(j); }));

  // 4) Data pribadi (wajib untuk semua jenis)
  tambahTeksPendek(form, 'NIK', true, '16 digit NIK', '3273010101010001');
  tambahTeksPendek(form, 'Nama', true, 'Nama lengkap sesuai KTP');
  tambahTeksPendek(form, 'Tempat Lahir', true, 'Contoh: Karawang');
  tambahTeksPendek(form, 'Tanggal Lahir', true, 'Format dd-mm-yyyy', '01-01-1990');
  tambahPilihan(form, 'JK', ['L', 'P'], true);
  tambahPilihan(form, 'Agama', ['Islam', 'Kristen', 'Katolik', 'Hindu', 'Buddha', 'Konghucu'], true);
  tambahPilihan(form, 'Status Perkawinan', ['Belum Kawin', 'Kawin', 'Cerai Hidup', 'Cerai Mati'], true);
  tambahTeksPendek(form, 'Pekerjaan', true, 'Contoh: Petani');
  tambahParagraf(form, 'Alamat', true, 'Dusun/Jalan, RT/RW, Desa, Kecamatan, Kabupaten');
  tambahParagraf(form, 'Keperluan', true, 'Alasan / kebutuhan surat');
  tambahTeksPendek(form, 'No. WhatsApp', true, 'Contoh: 081234567890', '6281234567890');
  tambahTeksPendek(form, 'Token', false, 'Jangan diubah bila sudah terisi dari tautan');

  // 5) Kolom tambahan per jenis surat (opsional - isi bila relevan)
  tambahTeksPendek(form, 'Pendidikan', false, 'Untuk Pengantar SKCK - contoh: SMA');
  tambahTeksPendek(form, 'Kewarganegaraan', false, 'Untuk Pengantar SKCK - contoh: WNI');
  tambahTeksPendek(form, 'Bidang Usaha', false, 'Untuk SKU - contoh: Warung Kelontong');
  tambahTeksPendek(form, 'Sejak Tahun', false, 'Untuk SKU - contoh: 2015');

  tambahTeksPendek(form, 'NIK Anak', false, 'Untuk Izin Orang Tua');
  tambahTeksPendek(form, 'Nama Anak', false, 'Untuk Izin Orang Tua');
  tambahTeksPendek(form, 'Tempat Lahir Anak', false, 'Untuk Izin Orang Tua');
  tambahTeksPendek(form, 'Tanggal Lahir Anak', false, 'Untuk Izin Orang Tua - dd-mm-yyyy');
  tambahPilihan(form, 'JK Anak', ['L', 'P'], false);
  tambahPilihan(form, 'Agama Anak', ['Islam', 'Kristen', 'Katolik', 'Hindu', 'Buddha', 'Konghucu'], false);
  tambahPilihan(form, 'Status Anak', ['Belum Kawin', 'Kawin', 'Cerai Hidup', 'Cerai Mati'], false);
  tambahTeksPendek(form, 'Pekerjaan Anak', false, 'Untuk Izin Orang Tua - contoh: Pelajar');
  tambahParagraf(form, 'Alamat Anak', false, 'Untuk Izin Orang Tua');
  tambahTeksPendek(form, 'Negara Tujuan', false, 'Untuk Izin Orang Tua - contoh: Malaysia');
  tambahTeksPendek(form, 'Nama PT', false, 'Untuk Izin Orang Tua (opsional)');

  tambahTeksPendek(form, 'Nama Instansi', false, 'Untuk Surat Instansi / lembaga');
  tambahParagraf(form, 'Alamat Instansi', false, 'Untuk Surat Instansi / lembaga');

  // 6) Tautkan jawaban formulir ke spreadsheet di atas
  form.setDestination(FormApp.DestinationType.SPREADSHEET, ssId);
  SpreadsheetApp.flush();

  // 7) Bagikan formulir agar warga bisa mengisi tanpa login Google
  try {
    DriveApp.getFileById(form.getId())
      .setSharing(DriveApp.Access.ANYONE_WITH_LINK, DriveApp.Permission.VIEW);
  } catch (e) {
    Logger.log('PERINGATAN: gagal mengatur form "siapa saja yang punya tautan": ' + e);
  }

  // 8) Temukan nama tab jawaban
  var namaSheet = SpreadsheetApp.openById(ssId).getSheets()
      .map(function (s) { return s.getName(); });
  var namaTabJawaban = '';
  namaSheet.forEach(function (n) {
    if (/response|respons/i.test(n)) namaTabJawaban = n;
  });
  if (!namaTabJawaban) {
    namaSheet.forEach(function (n) { if (n !== 'Sheet1') namaTabJawaban = n; });
  }
  if (!namaTabJawaban) namaTabJawaban = 'Form Responses 1';

  // 9) Tampilkan hasil
  var hasil =
    '==========================================================\n' +
    ' SELESAI! Salin nilai berikut ke Pengaturan Aplikasi.\n' +
    '==========================================================\n\n' +
    '1) Kolom "URL Google Formulir":\n' + form.getPublishedUrl() + '\n\n' +
    '2) Kolom "URL Sheet jawaban":\n' + ss.getUrl() + '\n\n' +
    '3) Kolom "Nama tab jawaban":\n' + namaTabJawaban + '\n\n' +
    'Tab pada spreadsheet: ' + namaSheet.join(' | ') + '\n' +
    'URL edit formulir (untuk Anda, bukan warga):\n' + form.getEditUrl() + '\n';

  Logger.log(hasil);
  return hasil;
}

// -- Fungsi bantu pembuat item ------------------------------

function tambahTeksPendek(form, judul, wajib, catatan, contoh) {
  var item = form.addTextItem().setTitle(judul).setRequired(wajib);
  var bantuan = catatan || '';
  if (contoh) bantuan = (bantuan ? bantuan + ' ' : '') + '(contoh: ' + contoh + ')';
  if (bantuan) item.setHelpText(bantuan);
  return item;
}

function tambahParagraf(form, judul, wajib, catatan) {
  var item = form.addParagraphTextItem().setTitle(judul).setRequired(wajib);
  if (catatan) item.setHelpText(catatan);
  return item;
}

function tambahPilihan(form, judul, pilihan, wajib) {
  var item = form.addMultipleChoiceItem().setTitle(judul).setRequired(wajib);
  item.setChoices(pilihan.map(function (p) { return item.createChoice(p); }));
  return item;
}
