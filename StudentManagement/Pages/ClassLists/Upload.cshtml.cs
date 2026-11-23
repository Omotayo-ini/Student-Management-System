using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OfficeOpenXml;
using StudentManagement.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StudentManagement.Pages.ClassLists
{
	public class UploadModel : PageModel
	{
		private readonly GradeRolloverService _rolloverService;
		private readonly ILogger<UploadModel> _logger;

		public UploadModel(GradeRolloverService rolloverService, ILogger<UploadModel> logger)
		{
			_rolloverService = rolloverService;
			_logger = logger;
		}

		[BindProperty]
		[Required]
		public IFormFile? UploadFile { get; set; }

		public ClassListImportResult? Result { get; set; }
		public string AcademicYear { get; set; } = DateTime.Now.Year.ToString();

		public void OnGet()
		{
		}

		public async Task<IActionResult> OnPostAsync()
		{
			if (UploadFile == null || UploadFile.Length == 0)
			{
				ModelState.AddModelError("", "Please select a file");
				return Page();
			}

			try
			{
				ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

				using var stream = UploadFile.OpenReadStream();
				using var package = new ExcelPackage(stream);
				var worksheet = package.Workbook.Worksheets.FirstOrDefault();

				if (worksheet == null)
				{
					ModelState.AddModelError("", "No worksheet found");
					return Page();
				}

				var entries = new List<ClassListEntry>();
				var lastRow = worksheet.Dimension?.End.Row ?? 0;
				var lastColumn = worksheet.Dimension?.End.Column ?? 0;
				var headerRow = FindHeaderRow(worksheet, lastRow, lastColumn);
				var dataStartRow = headerRow > 0 ? headerRow + 1 : 2;
				var columns = headerRow > 0
					? GetColumns(worksheet, headerRow, lastColumn)
					: new Dictionary<string, int>
					{
						["firstName"] = 1,
						["lastName"] = 2,
						["birthDate"] = 3,
						["grade"] = 6,
						["teacher"] = 7
					};

				for (int row = dataStartRow; row <= lastRow; row++)
				{
					var firstName = GetCellText(worksheet, row, columns, "firstName");
					var surname = GetCellText(worksheet, row, columns, "lastName");
					var grade = GetCellText(worksheet, row, columns, "grade");
					var teacher = GetCellText(worksheet, row, columns, "teacher");
					var className = GetCellText(worksheet, row, columns, "className");

					if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(surname))
						continue;

					DateTime? birthdate = null;
					var birthObj = columns.TryGetValue("birthDate", out var birthColumn)
						? worksheet.Cells[row, birthColumn].Value
						: null;
					if (birthObj is DateTime dt)
						birthdate = dt;
					else if (birthObj != null && DateTime.TryParse(birthObj.ToString(), out var parsed))
						birthdate = parsed;

					entries.Add(new ClassListEntry
					{
						FirstName = firstName,
						Surname = surname,
						Birthdate = birthdate,
						Grade = grade ?? "",
						ClassName = className ?? "",
						ClassTeacher = teacher ?? ""
					});
				}

				Result = await _rolloverService.ProcessClassListAsync(entries, AcademicYear);

				if (Result.Updated.Any())
				{
					TempData["SuccessMessage"] = $"Updated {Result.Updated.Count} students from class list.";
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing class list");
				ModelState.AddModelError("", $"Error: {ex.Message}");
			}

			return Page();
		}

		private static int FindHeaderRow(ExcelWorksheet worksheet, int lastRow, int lastColumn)
		{
			for (var row = 1; row <= Math.Min(lastRow, 5); row++)
			{
				var headers = Enumerable.Range(1, lastColumn)
					.Select(column => NormalizeHeader(worksheet.Cells[row, column].GetValue<string>()))
					.ToList();

				if (headers.Any(IsFirstNameHeader) && headers.Any(IsLastNameHeader))
					return row;
			}

			return 0;
		}

		private static Dictionary<string, int> GetColumns(ExcelWorksheet worksheet, int headerRow, int lastColumn)
		{
			var columns = new Dictionary<string, int>();
			for (var column = 1; column <= lastColumn; column++)
			{
				var header = NormalizeHeader(worksheet.Cells[headerRow, column].GetValue<string>());
				if (IsFirstNameHeader(header)) columns["firstName"] = column;
				else if (IsLastNameHeader(header)) columns["lastName"] = column;
				else if (header.Contains("birth") || header.Contains("dob")) columns["birthDate"] = column;
				else if (header.Contains("teacher")) columns["teacher"] = column;
				else if (header.Contains("class") || header.Contains("section")) columns["className"] = column;
				else if (header.Contains("grade") || header == "level") columns["grade"] = column;
			}

			return columns;
		}

		private static string GetCellText(ExcelWorksheet worksheet, int row, Dictionary<string, int> columns, string key)
		{
			return columns.TryGetValue(key, out var column)
				? worksheet.Cells[row, column].GetValue<string>()?.Trim() ?? string.Empty
				: string.Empty;
		}

		private static string NormalizeHeader(string? value)
		{
			return Regex.Replace(value?.Trim().ToLowerInvariant() ?? string.Empty, @"[^a-z0-9]", string.Empty);
		}

		private static bool IsFirstNameHeader(string header) =>
			header.Contains("firstname") || header == "first";

		private static bool IsLastNameHeader(string header) =>
			header.Contains("surname") || header.Contains("lastname") || header == "last";
	}
}