using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OnlineAssessment.Web.Models;

namespace OnlineAssessment.Web.Controllers
{
    [Authorize(Roles = "Organization")]
    public class CategoryQuestionsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CategoryQuestionsController> _logger;

        public CategoryQuestionsController(AppDbContext context, ILogger<CategoryQuestionsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: CategoryQuestions
        public async Task<IActionResult> Index()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return Unauthorized();
                }

                var organizationId = userId; // SapId is a string, no need to parse

                // Verify the organization exists - organizations don't need User records
                var organization = await _context.Organizations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.SapId == userId);

                if (organization == null)
                {
                    return BadRequest("Organization not found");
                }

                // PERFORMANCE OPTIMIZATION: Use a more efficient query that gets question counts directly
                // This avoids loading and deserializing the full JSON for each category
                var categoryQuestionsWithCounts = await _context.CategoryQuestions
                    .AsNoTracking()
                    .Where(cq => cq.CreatedBySapId == userId && CategoryQuestions.AllowedCategories.Contains(cq.Category))
                    .Select(cq => new
                    {
                        Id = cq.Id,
                        Category = cq.Category,
                        CreatedAt = cq.CreatedAt,
                        CreatedBySapId = cq.CreatedBySapId,
                        // Use string length as a rough estimate of question count
                        // MySQL doesn't support JsonLength directly, so we use string length as an approximation
                        EstimatedQuestionCount = cq.QuestionsJson.Length > 0
                            ? (cq.QuestionsJson.Length / 200) // Rough estimate based on average question size
                            : 0
                    })
                    .ToListAsync();

                // Convert to CategoryQuestions objects for the view
                var categoryQuestions = categoryQuestionsWithCounts.Select(cq => new CategoryQuestions
                {
                    Id = cq.Id,
                    Category = cq.Category,
                    CreatedAt = cq.CreatedAt,
                    CreatedBySapId = cq.CreatedBySapId,
                    // Set a placeholder Questions list with the estimated count
                    // This avoids loading the actual questions which is expensive
                    Questions = new List<QuestionDto>(new QuestionDto[cq.EstimatedQuestionCount])
                }).ToList();

                // Add aggressive response caching headers - safely
                try {
                    // Remove existing Cache-Control header if it exists to avoid duplication
                    Response.Headers.Remove("Cache-Control");
                    Response.Headers["Cache-Control"] = "private, max-age=300"; // Cache for 5 minutes
                }
                catch (Exception headerEx) {
                    _logger.LogWarning(headerEx, "Could not set Cache-Control header, continuing without it");
                }

                // Add performance metrics to ViewBag
                ViewBag.PerformanceMessage = "Optimized page load - question counts are estimated";

                return View(categoryQuestions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CategoryQuestions Index action");
                return BadRequest("An error occurred while loading category questions. Please try again later.");
            }
        }

        // GET: CategoryQuestions/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: CategoryQuestions/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CategoryQuestions categoryQuestions)
        {
            if (ModelState.IsValid)
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return Unauthorized();
                }

                var organizationId = userId; // SapId is a string, no need to parse

                // For organizations, we don't need a User record - they exist only in Organizations table
                // We'll use the organization SapId directly for CreatedBySapId

                // Verify the organization exists
                var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.SapId == userId);
                if (organization == null)
                {
                    ModelState.AddModelError("", "Organization not found");
                    return View(categoryQuestions);
                }

                // Validate category
                if (!CategoryQuestions.AllowedCategories.Contains(categoryQuestions.Category))
                {
                    ModelState.AddModelError("Category", "Invalid category selected.");
                }

                if (ModelState.IsValid)
                {
                    categoryQuestions.CreatedBySapId = userId;
                    categoryQuestions.CreatedAt = DateTime.UtcNow;

                    _context.Add(categoryQuestions);
                    await _context.SaveChangesAsync();
                    return RedirectToAction(nameof(Index));
                }
            }
            return View(categoryQuestions);
        }

        // API endpoint for uploading category questions
        [HttpPost]
        [Route("api/CategoryQuestions/Upload")]
        public async Task<IActionResult> UploadQuestions([FromBody] CategoryQuestionsUploadDto uploadDto)
        {
            try
            {
                _logger.LogInformation($"Upload questions called for category: {uploadDto?.Category}");

                // Basic validation
                if (string.IsNullOrWhiteSpace(uploadDto?.Category))
                {
                    _logger.LogWarning("Upload called with empty category");
                    return BadRequest(new { message = "Category is required" });
                }

                // Validate category
                if (!CategoryQuestions.AllowedCategories.Contains(uploadDto.Category))
                {
                    _logger.LogWarning($"Invalid category selected: {uploadDto.Category}");
                    return BadRequest(new { message = "Invalid category selected." });
                }

                if (uploadDto.Questions == null || !uploadDto.Questions.Any())
                {
                    _logger.LogWarning($"No questions provided for category: {uploadDto.Category}");
                    return BadRequest(new { message = "Questions are required" });
                }

                _logger.LogInformation($"Received {uploadDto.Questions.Count} questions for category: {uploadDto.Category}");

                // Validate that there are at least 60 questions
                if (uploadDto.Questions.Count < 60)
                {
                    _logger.LogWarning($"Not enough questions provided: {uploadDto.Questions.Count} (minimum 60)");
                    return BadRequest(new { message = "At least 60 questions are required in the question set" });
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    _logger.LogWarning("Upload called without user ID");
                    return Unauthorized();
                }

                var organizationId = userId; // SapId is a string, no need to parse
                _logger.LogInformation($"Upload: Organization ID: {organizationId}");

                // Verify the organization exists
                var organization = await _context.Organizations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.SapId == userId);

                if (organization == null)
                {
                    _logger.LogWarning($"Organization not found with ID: {organizationId}");
                    return BadRequest(new { message = "Organization not found" });
                }

                // Optimize JSON serialization - prepare it outside the database operations
                var jsonOptions = new JsonSerializerOptions {
                    ReferenceHandler = ReferenceHandler.Preserve,
                    MaxDepth = 64
                };

                // Pre-serialize the JSON to avoid doing it during database operations
                string questionsJson;
                try
                {
                    // Check for and remove duplicate questions before serializing
                    var uniqueQuestions = new List<QuestionDto>();
                    var seenIds = new HashSet<string>();

                    foreach (var question in uploadDto.Questions)
                    {
                        // Create a unique identifier for each question based on its text
                        string questionId = question.Text?.Trim() ?? "";

                        // Skip duplicate questions
                        if (string.IsNullOrEmpty(questionId) || seenIds.Contains(questionId))
                        {
                            _logger.LogWarning($"Skipping duplicate or empty question: {questionId}");
                            continue;
                        }

                        seenIds.Add(questionId);
                        uniqueQuestions.Add(question);
                    }

                    int removedCount = uploadDto.Questions.Count - uniqueQuestions.Count;
                    _logger.LogInformation($"After removing duplicates: {uniqueQuestions.Count} questions (removed {removedCount})");

                    // Make sure we still have enough questions after deduplication
                    if (uniqueQuestions.Count < 60)
                    {
                        _logger.LogWarning($"Not enough unique questions after deduplication: {uniqueQuestions.Count} (minimum 60)");
                        return BadRequest(new {
                            message = $"After removing duplicates, only {uniqueQuestions.Count} unique questions remain. At least 60 unique questions are required."
                        });
                    }

                    // Use the deduplicated list
                    uploadDto.Questions = uniqueQuestions;

                    // Add more options to handle potential issues
                    jsonOptions.PropertyNameCaseInsensitive = true;
                    jsonOptions.AllowTrailingCommas = true;
                    jsonOptions.ReadCommentHandling = JsonCommentHandling.Skip;
                    jsonOptions.WriteIndented = true; // Make JSON more readable for debugging

                    questionsJson = JsonSerializer.Serialize(uploadDto.Questions, jsonOptions);
                    _logger.LogInformation($"Successfully serialized {uploadDto.Questions.Count} questions, JSON length: {questionsJson.Length}");

                    // Validate that the JSON can be deserialized back
                    var testDeserialization = JsonSerializer.Deserialize<List<QuestionDto>>(questionsJson, jsonOptions);
                    if (testDeserialization == null || testDeserialization.Count == 0)
                    {
                        _logger.LogWarning("JSON serialized but deserialization test failed");
                        return BadRequest(new { message = "Error validating question format" });
                    }

                    _logger.LogInformation($"Deserialization test successful: {testDeserialization.Count} questions");
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "JSON serialization error");
                    return BadRequest(new { message = "Error processing questions: " + jsonEx.Message });
                }

                // Check if category questions already exist for this organization
                // Use a more efficient query with AsNoTracking for the check
                bool categoryExists = await _context.CategoryQuestions
                    .AsNoTracking()
                    .AnyAsync(cq => cq.Category == uploadDto.Category && cq.CreatedBySapId == userId);

                if (categoryExists)
                {
                    _logger.LogInformation($"Updating existing category '{uploadDto.Category}' for organization {organizationId}");

                    // Use direct SQL update for better performance on large JSON data
                    // This avoids loading the entire JSON into memory for tracking
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE CategoryQuestions SET QuestionsJson = {0} WHERE Category = {1} AND CreatedBySapId = {2}",
                        questionsJson, uploadDto.Category, userId);
                }
                else
                {
                    _logger.LogInformation($"Creating new category '{uploadDto.Category}' for organization {organizationId}");

                    // Create new category questions
                    var categoryQuestions = new CategoryQuestions
                    {
                        Category = uploadDto.Category,
                        QuestionsJson = questionsJson,
                        CreatedBySapId = userId,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.Add(categoryQuestions);
                    await _context.SaveChangesAsync();
                }

                // Clear response cache for this category - safely
                try {
                    // Remove existing Cache-Control header if it exists to avoid duplication
                    Response.Headers.Remove("Cache-Control");
                    Response.Headers["Cache-Control"] = "no-cache, no-store";
                }
                catch (Exception headerEx) {
                    _logger.LogWarning(headerEx, "Could not set Cache-Control header, continuing without it");
                }

                _logger.LogInformation($"Questions uploaded successfully for category '{uploadDto.Category}'");
                return Ok(new { message = "Questions uploaded successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading category questions");
                return StatusCode(500, new { message = "Error uploading questions: " + ex.Message });
            }
        }

        // API endpoint for getting category questions
        [HttpGet]
        [Route("api/CategoryQuestions/GetByCategory")]
        [ResponseCache(Duration = 60)] // Reduced cache time to 1 minute for testing
        public async Task<IActionResult> GetQuestionsByCategory(string category, int? limit = null, int? offset = null)
        {
            try
            {
                _logger.LogInformation($"GetByCategory called for category: {category}");

                if (string.IsNullOrWhiteSpace(category))
                {
                    _logger.LogWarning("GetByCategory called with empty category");
                    return BadRequest(new { message = "Category is required" });
                }

                // Validate category
                if (!CategoryQuestions.AllowedCategories.Contains(category))
                {
                    _logger.LogWarning($"Invalid category selected: {category}");
                    return BadRequest(new { message = "Invalid category selected." });
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    _logger.LogWarning("GetByCategory called without user ID");
                    return Unauthorized();
                }

                var organizationId = userId; // SapId is a string, no need to parse
                _logger.LogInformation($"GetByCategory: Organization ID: {organizationId}");

                // Verify the organization exists
                var organization = await _context.Organizations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.SapId == userId);
                if (organization == null)
                {
                    _logger.LogWarning($"Organization not found with ID: {organizationId}");
                    return BadRequest(new { message = "Organization not found" });
                }

                // Check if there are any category questions for this organization
                var categoryCount = await _context.CategoryQuestions
                    .AsNoTracking()
                    .CountAsync(cq => cq.CreatedBySapId == userId);

                _logger.LogInformation($"Total category questions for organization {organizationId}: {categoryCount}");

                // PERFORMANCE OPTIMIZATION: Use AsNoTracking and only select the fields we need
                var categoryQuestionsQuery = await _context.CategoryQuestions
                    .AsNoTracking()
                    .Where(cq => cq.Category == category && cq.CreatedBySapId == userId)
                    .Select(cq => new { cq.QuestionsJson, cq.Id, cq.CreatedAt })
                    .FirstOrDefaultAsync();

                if (categoryQuestionsQuery == null)
                {
                    _logger.LogWarning($"No questions found for category '{category}' and organization {organizationId}");

                    // Check if the category exists for any organization
                    var categoryExists = await _context.CategoryQuestions
                        .AsNoTracking()
                        .AnyAsync(cq => cq.Category == category);

                    if (categoryExists)
                    {
                        _logger.LogInformation($"Category '{category}' exists but not for organization {organizationId}");
                    }

                    return NotFound(new { message = "No questions found for this category" });
                }

                _logger.LogInformation($"Found questions for category '{category}', created at {categoryQuestionsQuery.CreatedAt}");

                // PERFORMANCE OPTIMIZATION: Add caching headers - safely
                try {
                    // Remove existing Cache-Control header if it exists to avoid duplication
                    Response.Headers.Remove("Cache-Control");
                    Response.Headers["Cache-Control"] = "private, max-age=60"; // Reduced to 1 minute for testing
                }
                catch (Exception headerEx) {
                    _logger.LogWarning(headerEx, "Could not set Cache-Control header, continuing without it");
                }

                // PERFORMANCE OPTIMIZATION: Use a more efficient JSON deserialization
                var options = new JsonSerializerOptions {
                    ReferenceHandler = ReferenceHandler.Preserve,
                    MaxDepth = 64
                };

                try
                {
                    // Add more options to handle potential issues
                    options.PropertyNameCaseInsensitive = true;
                    options.AllowTrailingCommas = true;
                    options.ReadCommentHandling = JsonCommentHandling.Skip;

                    var allQuestions = JsonSerializer.Deserialize<List<QuestionDto>>(categoryQuestionsQuery.QuestionsJson, options);

                    if (allQuestions == null || allQuestions.Count == 0)
                    {
                        _logger.LogWarning($"JSON deserialized but no questions found for category '{category}'");
                        return NotFound(new { message = "No questions found in this category after deserialization" });
                    }

                    _logger.LogInformation($"Successfully deserialized {allQuestions.Count} questions for category '{category}'");

                    // Check for duplicate questions and remove them
                    var uniqueQuestions = new List<QuestionDto>();
                    var seenIds = new HashSet<string>();

                    foreach (var question in allQuestions)
                    {
                        // Create a unique identifier for each question based on its text
                        string questionId = question.Text?.Trim() ?? "";

                        // Skip duplicate questions
                        if (string.IsNullOrEmpty(questionId) || seenIds.Contains(questionId))
                        {
                            _logger.LogWarning($"Skipping duplicate or empty question: {questionId}");
                            continue;
                        }

                        seenIds.Add(questionId);
                        uniqueQuestions.Add(question);
                    }

                    _logger.LogInformation($"After removing duplicates: {uniqueQuestions.Count} questions (removed {allQuestions.Count - uniqueQuestions.Count})");

                    // Use the deduplicated list
                    allQuestions = uniqueQuestions;

                    // PERFORMANCE OPTIMIZATION: Implement pagination
                    int totalCount = allQuestions.Count;

                    // Create a list for the paginated questions
                    List<QuestionDto> paginatedQuestions;

                    // Apply pagination if requested
                    if (limit.HasValue)
                    {
                        int skip = offset.HasValue ? offset.Value : 0;
                        int take = limit.Value;

                        // Get the paginated questions
                        paginatedQuestions = allQuestions
                            .Skip(skip)
                            .Take(take)
                            .ToList();

                        _logger.LogInformation($"Returning {paginatedQuestions.Count} questions (paginated) out of {totalCount} total");
                    }
                    else
                    {
                        // If no pagination, use all questions
                        paginatedQuestions = allQuestions;
                        _logger.LogInformation($"Returning all {totalCount} questions (no pagination)");
                    }

                    // For test creation, we need to ensure we always return at least an empty array
                    // even if we're just checking if questions exist
                    if (paginatedQuestions == null)
                    {
                        paginatedQuestions = new List<QuestionDto>();
                    }

                    // Return metadata along with the questions
                    // If limit is 0, it means we're just checking if questions exist, so return an empty array
                    // but still include the totalCount so the client knows questions exist
                    return Ok(new {
                        questions = limit.HasValue && limit.Value == 0 ? new List<QuestionDto>() : paginatedQuestions,
                        totalCount = totalCount,
                        hasMore = limit.HasValue && offset.HasValue && (offset.Value + limit.Value < totalCount)
                    });
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, $"JSON deserialization error for category '{category}'");

                    // Try to get the first 100 characters of the JSON to help with debugging
                    string jsonPreview = categoryQuestionsQuery.QuestionsJson.Length > 100
                        ? categoryQuestionsQuery.QuestionsJson.Substring(0, 100) + "..."
                        : categoryQuestionsQuery.QuestionsJson;

                    _logger.LogInformation($"JSON preview: {jsonPreview}");

                    return StatusCode(500, new { message = "Error deserializing questions: " + jsonEx.Message });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting category questions");
                return StatusCode(500, new { message = "Error getting questions: " + ex.Message });
            }
        }

        // API endpoint for getting all categories
        [HttpGet]
        [Route("api/CategoryQuestions/GetCategories")]
        [ResponseCache(Duration = 300)] // Cache for 5 minutes
        public async Task<IActionResult> GetCategories()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return Unauthorized();
                }

                var organizationId = userId; // SapId is a string, no need to parse

                // Verify the organization exists
                var organization = await _context.Organizations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.SapId == userId);
                if (organization == null)
                {
                    return BadRequest(new { message = "Organization not found" });
                }

                // PERFORMANCE OPTIMIZATION: Use AsNoTracking and add DISTINCT to avoid duplicates
                var categories = await _context.CategoryQuestions
                    .AsNoTracking()
                    .Where(cq => cq.CreatedBySapId == userId && CategoryQuestions.AllowedCategories.Contains(cq.Category))
                    .Select(cq => cq.Category)
                    .Distinct()
                    .ToListAsync();

                // PERFORMANCE OPTIMIZATION: Add caching headers - safely
                try {
                    // Remove existing Cache-Control header if it exists to avoid duplication
                    Response.Headers.Remove("Cache-Control");
                    Response.Headers["Cache-Control"] = "private, max-age=300"; // Cache for 5 minutes
                }
                catch (Exception headerEx) {
                    _logger.LogWarning(headerEx, "Could not set Cache-Control header, continuing without it");
                }

                return Ok(new {
                    categories,
                    // Add timestamp for client-side cache validation
                    timestamp = DateTime.UtcNow.ToString("o")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting categories");
                return StatusCode(500, new { message = "Error getting categories: " + ex.Message });
            }
        }

        // DELETE: CategoryQuestions/Delete/5
        [HttpDelete]
        [Route("CategoryQuestions/Delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return Json(new { success = false, message = "Unauthorized: Organization access required." });
                }

                var organizationId = userId; // SapId is a string, no need to parse

                // Verify the organization exists
                var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.SapId == userId);
                if (organization == null)
                {
                    return Json(new { success = false, message = "Organization not found" });
                }

                var categoryQuestions = await _context.CategoryQuestions.FindAsync(id);

                if (categoryQuestions == null)
                {
                    return Json(new { success = false, message = "Question set not found." });
                }

                // Verify ownership
                if (categoryQuestions.CreatedBySapId != userId)
                {
                    return Json(new { success = false, message = "You can only delete your own question sets." });
                }

                _context.CategoryQuestions.Remove(categoryQuestions);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Question set deleted successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in Delete action for category questions {id}");
                return Json(new { success = false, message = "An unexpected error occurred while deleting the question set." });
            }
        }
    }
}
