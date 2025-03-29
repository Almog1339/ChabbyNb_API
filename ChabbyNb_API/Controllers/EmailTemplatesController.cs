using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "RequireAdminRole")]
public class EmailTemplatesController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<EmailTemplatesController> _logger;

    public EmailTemplatesController(
        IWebHostEnvironment environment,
        ILogger<EmailTemplatesController> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    // GET: api/EmailTemplates
    [HttpGet]
    public ActionResult<IEnumerable<EmailTemplateInfo>> GetAllTemplates()
    {
        var templatesPath = Path.Combine(_environment.ContentRootPath, "EmailTemplates");
        if (!Directory.Exists(templatesPath))
        {
            Directory.CreateDirectory(templatesPath);
            return new List<EmailTemplateInfo>();
        }

        var templateFiles = Directory.GetFiles(templatesPath, "*.html");
        var templates = templateFiles.Select(file => new EmailTemplateInfo
        {
            Name = Path.GetFileNameWithoutExtension(file),
            Path = file,
            LastModified = System.IO.File.GetLastWriteTime(file)
        }).ToList();

        return templates;
    }

    // GET: api/EmailTemplates/{name}
    [HttpGet("{name}")]
    public async Task<ActionResult<EmailTemplateContent>> GetTemplate(string name)
    {
        var templatePath = Path.Combine(_environment.ContentRootPath, "EmailTemplates", $"{name}.html");
        if (!System.IO.File.Exists(templatePath))
        {
            return NotFound($"Template '{name}' not found");
        }

        string content = await System.IO.File.ReadAllTextAsync(templatePath);
        return new EmailTemplateContent
        {
            Name = name,
            Content = content,
            LastModified = System.IO.File.GetLastWriteTime(templatePath)
        };
    }

    // PUT: api/EmailTemplates/{name}
    [HttpPut("{name}")]
    public async Task<IActionResult> UpdateTemplate(string name, [FromBody] EmailTemplateUpdateDto template)
    {
        if (name != template.Name)
        {
            return BadRequest("Template name mismatch");
        }

        var templatePath = Path.Combine(_environment.ContentRootPath, "EmailTemplates", $"{name}.html");

        try
        {
            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(templatePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write the template content
            await System.IO.File.WriteAllTextAsync(templatePath, template.Content);
            _logger.LogInformation($"Email template '{name}' updated by admin");

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating email template '{name}'");
            return StatusCode(500, "Error updating template");
        }
    }

    // POST: api/EmailTemplates
    [HttpPost]
    public async Task<ActionResult<EmailTemplateInfo>> CreateTemplate([FromBody] EmailTemplateCreateDto template)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            return BadRequest("Template name cannot be empty");
        }

        // Sanitize the template name for use as a filename
        string sanitizedName = SanitizeFileName(template.Name);
        var templatePath = Path.Combine(_environment.ContentRootPath, "EmailTemplates", $"{sanitizedName}.html");

        // Check if template already exists
        if (System.IO.File.Exists(templatePath))
        {
            return Conflict($"Template '{sanitizedName}' already exists");
        }

        try
        {
            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(templatePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write the template content
            await System.IO.File.WriteAllTextAsync(templatePath, template.Content);
            _logger.LogInformation($"Email template '{sanitizedName}' created by admin");

            var templateInfo = new EmailTemplateInfo
            {
                Name = sanitizedName,
                Path = templatePath,
                LastModified = System.IO.File.GetLastWriteTime(templatePath)
            };

            return CreatedAtAction(nameof(GetTemplate), new { name = sanitizedName }, templateInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating email template '{sanitizedName}'");
            return StatusCode(500, "Error creating template");
        }
    }

    private string SanitizeFileName(string fileName)
    {
        // Remove invalid file name characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}

public class EmailTemplateInfo
{
    public string Name { get; set; }
    public string Path { get; set; }
    public DateTime LastModified { get; set; }
}

public class EmailTemplateContent
{
    public string Name { get; set; }
    public string Content { get; set; }
    public DateTime LastModified { get; set; }
}

public class EmailTemplateUpdateDto
{
    public string Name { get; set; }
    public string Content { get; set; }
}

public class EmailTemplateCreateDto
{
    public string Name { get; set; }
    public string Content { get; set; }
}