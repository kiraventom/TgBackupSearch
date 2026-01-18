using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Tesseract;
using TgBackupSearch.Model;

namespace TgBackupSearch.Recognition;

public record OcrToken(string Text, float Confidence);

