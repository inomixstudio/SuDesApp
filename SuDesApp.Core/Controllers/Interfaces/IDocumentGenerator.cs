using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuDesApp.Controllers.Interfaces
{
    public interface IDocumentGenerator
    {
        Task GenerateAsync(Stream outputStream, string documentType);
    }
}
