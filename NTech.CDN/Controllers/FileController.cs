using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Processing;

namespace NTech.CDN.Controllers
{
    [Route("files")]
    [ApiController]
    public class FileController : ControllerBase
    {
        private readonly String basePath;

        public FileController(IConfiguration configuration)
        {
            this.basePath = configuration["FilePath"]!;
        }

        [HttpGet("{*path}")]
        public async Task<IActionResult> GetFile(string path, [FromQuery] string? scale)
        {
            if (string.IsNullOrEmpty(path))
                return BadRequest("No path provided!");

            // Combine & normalise paths.
            var fullPath = Path.Combine(basePath, path);
            var normalizedBase = Path.GetFullPath(basePath);
            var normalizedFull = Path.GetFullPath(fullPath);

            // Security check against access outside the base path.
            if (!normalizedFull.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            if (!System.IO.File.Exists(normalizedFull))
                return NotFound("File not found.");

            var contentType = GetContentType(normalizedFull);

            // Only allow scaling if the file is an actual image.
            if (!string.IsNullOrWhiteSpace(scale) &&
                contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseScale(scale, out double factor))
                    return BadRequest("Invalid scale format. Use e.g. '50%' or '0.5'.");

                try
                {
                    using var image = await Image.LoadAsync(normalizedFull);
                    var newWidth = (int)(image.Width * factor);
                    var newHeight = (int)(image.Height * factor);

                    image.Mutate(x => x.Resize(newWidth, newHeight));

                    using var ms = new MemoryStream();
                    await image.SaveAsync(ms, image.DetectEncoder(normalizedFull));
                    ms.Position = 0;

                    return File(ms.ToArray(), contentType);
                }
                catch (UnknownImageFormatException)
                {
                    // If the file is not a valid image, it returns the raw file.
                    return PhysicalFile(normalizedFull, "application/octet-stream");
                }
            }

            // Returns the physical file, with no change.
            return PhysicalFile(normalizedFull, contentType);
        }

        //[HttpPost("Upload")]
        //public async Task<IActionResult> Upload(List<IFormFile> files, [FromForm] string? path)
        //{
        //    if (string.IsNullOrWhiteSpace(path))
        //        path = "/";

        //    // Normaliser sti og fjern forsøg på directory traversal
        //    path = path.TrimStart('/').Replace("..", "");

        //    var savePath = Path.Combine(basePath, path);
        //    Directory.CreateDirectory(savePath);

        //    foreach (var file in files)
        //    {
        //        var filePath = Path.Combine(savePath, file.FileName);
        //        using var stream = System.IO.File.Create(filePath);
        //        await file.CopyToAsync(stream);
        //    }

        //    return Ok(new { message = "Upload complete", savedTo = savePath });
        //}

        [HttpPost("Upload")]
        public async Task<IActionResult> Upload(List<IFormFile> files, [FromForm] string? path, [FromForm] string[]? dirs)
        {
            if (string.IsNullOrWhiteSpace(path))
                path = "/";

            // Normalisér og fjern traversal
            path = path.TrimStart('/').Replace("..", "");

            var baseUploadPath = Path.Combine(basePath, path);

            // 1) Opret mapper først (inkl. tomme)
            if (dirs is not null)
            {
                foreach (var d in dirs)
                {
                    var safeDir = (d ?? string.Empty)
                        .Replace("\\", "/")
                        .Replace("..", "")
                        .TrimStart('/', '\\')
                        .Trim();

                    if (safeDir.Length == 0) continue;

                    var fullDirPath = Path.Combine(baseUploadPath, safeDir);
                    Directory.CreateDirectory(fullDirPath);
                }
            }

            // 2) Gem filer i deres relative placering
            foreach (var file in files)
            {
                var safeRelativePath = (file.FileName ?? string.Empty)
                    .Replace("\\", "/")
                    .Replace("..", "")
                    .TrimStart('/', '\\');

                var fullSavePath = Path.Combine(baseUploadPath, safeRelativePath);

                Directory.CreateDirectory(Path.GetDirectoryName(fullSavePath)!);

                using var stream = System.IO.File.Create(fullSavePath);
                await file.CopyToAsync(stream);
            }

            return Ok(new { message = "Upload complete", savedTo = baseUploadPath });
        }

        /// <summary>
        /// Attempts to parse a <see cref="string"/> to a scale factor.
        /// </summary>
        /// <param name="scale"><see cref="string"/> with the format of either 50% or 0.5.</param>
        /// <param name="factor">The output to store the scale factor in.</param>
        /// <returns><see cref="bool"/> if the parse was successful.</returns>
        private bool TryParseScale(string scale, out double factor)
        {
            scale = scale.Trim();
            if (scale.EndsWith("%") && double.TryParse(scale.TrimEnd('%'), out double pct))
            {
                factor = pct / 100.0;
                return true;
            }
            return double.TryParse(scale, out factor);
        }

        /// <summary>
        /// Finds the mimetype of a given file.
        /// </summary>
        /// <param name="path">The path of the file.</param>
        /// <returns>The files mimetype. If not successful, returns application/octet-stream instead.</returns>
        private string GetContentType(string path)
        {
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(path, out var contentType))
            {
                contentType = "application/octet-stream";
            }
            return contentType;
        }
    }
}
