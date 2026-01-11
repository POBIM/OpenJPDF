// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;

namespace OpenJPDF.Services;

public class OcrService : IDisposable
{
    private TesseractEngine? _engine;
    private bool _disposed;
    private static readonly string TessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

    public bool IsInitialized => _engine != null;

    /// <summary>
    /// Initialize OCR engine with specified language
    /// </summary>
    public bool Initialize(string language = "eng+tha")
    {
        try
        {
            // Check if tessdata folder exists
            if (!Directory.Exists(TessDataPath))
            {
                Directory.CreateDirectory(TessDataPath);
                System.Diagnostics.Debug.WriteLine($"Created tessdata folder at: {TessDataPath}");
                return false; // Need to download data files
            }

            // Check for required data files
            var engFile = Path.Combine(TessDataPath, "eng.traineddata");
            var thaFile = Path.Combine(TessDataPath, "tha.traineddata");
            
            if (!File.Exists(engFile))
            {
                System.Diagnostics.Debug.WriteLine($"Missing: {engFile}");
                return false;
            }

            _engine = new TesseractEngine(TessDataPath, language, EngineMode.Default);
            _engine.SetVariable("tessedit_char_whitelist", "");
            _engine.SetVariable("preserve_interword_spaces", "1");
            
            System.Diagnostics.Debug.WriteLine("OCR Engine initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize OCR: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Perform OCR on a bitmap image
    /// </summary>
    public string RecognizeText(DrawingBitmap bitmap)
    {
        if (_engine == null)
        {
            throw new InvalidOperationException("OCR engine not initialized. Call Initialize() first.");
        }

        try
        {
            using var pix = Pix.LoadFromMemory(BitmapToBytes(bitmap));
            using var page = _engine.Process(pix);
            
            var text = page.GetText();
            var confidence = page.GetMeanConfidence();
            
            System.Diagnostics.Debug.WriteLine($"OCR Result (confidence: {confidence:P}): {text}");
            
            return text.Trim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OCR error: {ex.Message}");
            return string.Empty;
        }
    }

    private static byte[] BitmapToBytes(DrawingBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>
    /// Perform OCR on a region of a bitmap
    /// </summary>
    public string RecognizeTextInRegion(DrawingBitmap sourceBitmap, DrawingRectangle region)
    {
        if (_engine == null)
        {
            throw new InvalidOperationException("OCR engine not initialized. Call Initialize() first.");
        }

        try
        {
            // Ensure region is within bounds
            region.X = Math.Max(0, region.X);
            region.Y = Math.Max(0, region.Y);
            region.Width = Math.Min(region.Width, sourceBitmap.Width - region.X);
            region.Height = Math.Min(region.Height, sourceBitmap.Height - region.Y);

            if (region.Width <= 0 || region.Height <= 0)
            {
                return string.Empty;
            }

            // Crop the region
            using var croppedBitmap = sourceBitmap.Clone(region, sourceBitmap.PixelFormat);
            
            return RecognizeText(croppedBitmap);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OCR region error: {ex.Message}");
            return string.Empty;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _engine?.Dispose();
            _engine = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

public sealed class BackgroundRemovalService : IDisposable
{
    private static readonly string DefaultModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "snap.onnx");
    private readonly object _syncRoot = new();
    private readonly string _modelPath;
    private InferenceSession? _session;
    private string? _inputName;
    private string? _outputName;
    private int _inputWidth = 320;
    private int _inputHeight = 320;
    private bool _disposed;

    public BackgroundRemovalService(string? modelPath = null)
    {
        _modelPath = string.IsNullOrWhiteSpace(modelPath) ? DefaultModelPath : modelPath;
    }

    public string ModelPath => _modelPath;

    public bool IsInitialized => _session != null;

    public async Task<(bool Success, byte[]? ImageBytes, string? ErrorMessage)> RemoveBackgroundAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return (false, null, "Image path is empty.");
        }

        if (!File.Exists(imagePath))
        {
            return (false, null, "Image file not found.");
        }

        if (!TryInitialize(out var initError))
        {
            return (false, null, initError);
        }

        try
        {
            using var original = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(imagePath, cancellationToken);
            int originalWidth = original.Width;
            int originalHeight = original.Height;

            // Flatten alpha to white background for model input (handles PNGs with existing transparency)
            using var flattened = new SixLabors.ImageSharp.Image<Rgb24>(originalWidth, originalHeight, new Rgb24(255, 255, 255));
            flattened.Mutate(ctx => ctx.DrawImage(original, 1f));
            
            using var resized = flattened.Clone();
            resized.Mutate(context => context.Resize(_inputWidth, _inputHeight));

            var inputTensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });
            resized.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < _inputHeight; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < _inputWidth; x++)
                    {
                        var pixel = row[x];
                        inputTensor[0, 0, y, x] = NormalizeChannel(pixel.R, 0);
                        inputTensor[0, 1, y, x] = NormalizeChannel(pixel.G, 1);
                        inputTensor[0, 2, y, x] = NormalizeChannel(pixel.B, 2);
                    }
                }
            });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName!, inputTensor)
            };

            using var results = _session!.Run(inputs);
            var output = results.First().AsTensor<float>();
            var mask = ExtractMask(output, out int maskWidth, out int maskHeight);

            using var maskImage = new SixLabors.ImageSharp.Image<L8>(maskWidth, maskHeight);
            PopulateMask(maskImage, mask, maskWidth, maskHeight);

            maskImage.Mutate(context =>
            {
                context.Resize(originalWidth, originalHeight);
                context.GaussianBlur(1.0f);
            });

            ApplyMask(original, maskImage);

            using var outputStream = new MemoryStream();
            await original.SaveAsPngAsync(outputStream, cancellationToken);
            return (true, outputStream.ToArray(), null);
        }
        catch (Exception ex)
        {
            return (false, null, $"Background removal failed: {ex.Message}");
        }
    }

    private bool TryInitialize(out string error)
    {
        error = string.Empty;
        if (_session != null)
        {
            return true;
        }

        lock (_syncRoot)
        {
            if (_session != null)
            {
                return true;
            }

            if (!File.Exists(_modelPath))
            {
                error = $"Background removal model not found at: {_modelPath}\n\n" +
                        "Please reinstall OpenJPDF to restore the model file.";
                return false;
            }

            try
            {
                _session = new InferenceSession(_modelPath);
                var inputMetadata = _session.InputMetadata.First();
                _inputName = inputMetadata.Key;
                var dims = inputMetadata.Value.Dimensions.ToArray();
                if (dims.Length >= 4)
                {
                    _inputHeight = GetDimensionValue(dims[^2], 320);
                    _inputWidth = GetDimensionValue(dims[^1], 320);
                }

                _outputName = _session.OutputMetadata.Keys.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(_outputName))
                {
                    error = "Background removal model output not found.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to initialize background removal model: {ex.Message}";
                return false;
            }
        }
    }

    // RMBG-1.4 uses simple 0-1 normalization (no ImageNet mean/std)
    private static float NormalizeChannel(byte value, int channelIndex)
    {
        // Simple normalization: 0-255 -> 0-1
        return value / 255f;
    }

    private static int GetDimensionValue(long dimension, int fallback)
    {
        if (dimension <= 0)
        {
            return fallback;
        }

        if (dimension > int.MaxValue)
        {
            return fallback;
        }

        return (int)dimension;
    }

    private static float[] ExtractMask(Tensor<float> output, out int width, out int height)
    {
        var dims = output.Dimensions.ToArray();
        if (dims.Length >= 4)
        {
            height = dims[^2];
            width = dims[^1];
            var mask = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    mask[y * width + x] = output[0, 0, y, x];
                }
            }
            return NormalizeMask(mask);
        }

        if (dims.Length == 3)
        {
            height = dims[^2];
            width = dims[^1];
            var mask = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    mask[y * width + x] = output[0, y, x];
                }
            }
            return NormalizeMask(mask);
        }

        if (dims.Length == 2)
        {
            height = dims[0];
            width = dims[1];
            var mask = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    mask[y * width + x] = output[y, x];
                }
            }
            return NormalizeMask(mask);
        }

        width = 1;
        height = 1;
        return new float[] { 1f };
    }

    private static float[] NormalizeMask(float[] mask)
    {
        float min = mask.Min();
        float max = mask.Max();
        bool needsSigmoid = max > 1f || min < 0f;
        if (!needsSigmoid)
        {
            return mask;
        }

        var normalized = new float[mask.Length];
        for (int i = 0; i < mask.Length; i++)
        {
            normalized[i] = 1f / (1f + MathF.Exp(-mask[i]));
        }
        return normalized;
    }

    private static void PopulateMask(SixLabors.ImageSharp.Image<L8> maskImage, float[] mask, int width, int height)
    {
        int index = 0;
        maskImage.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    float value = mask[index++];
                    byte alpha = (byte)Math.Clamp(value * 255f, 0f, 255f);
                    row[x] = new L8(alpha);
                }
            }
        });
    }

    private static void ApplyMask(SixLabors.ImageSharp.Image<Rgba32> original, SixLabors.ImageSharp.Image<L8> mask)
    {
        var alphaValues = new byte[mask.Width * mask.Height];
        int alphaIndex = 0;

        mask.ProcessPixelRows(maskAccessor =>
        {
            for (int y = 0; y < mask.Height; y++)
            {
                var maskRow = maskAccessor.GetRowSpan(y);
                for (int x = 0; x < mask.Width; x++)
                {
                    alphaValues[alphaIndex++] = maskRow[x].PackedValue;
                }
            }
        });

        original.ProcessPixelRows(originalAccessor =>
        {
            int index = 0;
            for (int y = 0; y < original.Height; y++)
            {
                var pixels = originalAccessor.GetRowSpan(y);
                for (int x = 0; x < original.Width; x++)
                {
                    byte alpha = alphaValues[index++];
                    byte originalAlpha = pixels[x].A;
                    pixels[x].A = (byte)(originalAlpha * (alpha / 255f));
                }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session?.Dispose();
        _session = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
