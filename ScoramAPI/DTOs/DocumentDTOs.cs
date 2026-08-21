using Microsoft.AspNetCore.Http;

namespace ScoramAPI.DTOs
{
    // Bound as a single [FromForm] model on DocumentsController.Upload instead of two separate
    // [FromForm] parameters (IFormFile file, string category). Swashbuckle.AspNetCore's SwaggerGen
    // cannot reliably generate a multipart/form-data schema when an action mixes an [FromForm]
    // IFormFile parameter with another standalone [FromForm] parameter -- it throws
    // SwaggerGeneratorException ("[FromForm] attribute used with IFormFile") while building
    // /swagger/v1/swagger.json, which is exactly the 500 seen from the Swagger UI in production.
    // Wrapping every form field in one request model is the officially documented fix (see
    // https://github.com/domaindrivendev/Swashbuckle.AspNetCore#handle-forms-and-file-uploads).
    // The wire format is unchanged: multipart/form-data with fields "file" and "category" --
    // ASP.NET Core model binding maps the two form fields onto these two properties the same way it
    // did with the separate parameters, so no client changes are needed.
    public class DocumentUploadRequestDto
    {
        public IFormFile File { get; set; } = default!;
        public string Category { get; set; } = string.Empty;
    }


    // Response envelope for the Documents endpoints specifically (Step 10's exact
    // {success, message, data} shape). Deliberately scoped to this one feature rather than a
    // codebase-wide convention change -- every other controller already has its own established
    // response shape (plain DTOs / NoContent / ProblemDetails), and Step 21 says not to touch
    // unrelated APIs.
    public class DocumentApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    public class DocumentUploadResponseDto
    {
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string BlobName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }
}
