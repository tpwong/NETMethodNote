using PdfiumViewer;
using System.Drawing;
using System.Drawing.Imaging;

namespace PdfToPngConverter
{
    class Program
    {
        static void Main(string[] args)
        {
            // Set PDF path and output directory
            string pdfPath = @"C:\Users\tpwong\Desktop\406.pdf";  // Path to your PDF file
            string outputFolder = @"C:\Users\tpwong\Desktop";     // Path to your output directory

            try
            {
                // Call the conversion method
                ConvertPdfToPngUsingPdfium(pdfPath, outputFolder);
                Console.WriteLine("Conversion completed, press any key to exit...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.ReadKey();
        }

        /// <summary>
        /// Convert PDF file to PNG images using PdfiumViewer
        /// </summary>
        public static void ConvertPdfToPngUsingPdfium(string pdfPath, string outputFolder, int dpi = 300)
        {
            // Check if the PDF file exists
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("The specified PDF file could not be found", pdfPath);
            }

            // Ensure the output directory exists
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            // Calculate scaling factor (PDF default is 72 DPI)
            float scale = dpi / 72f;

            try
            {
                // Load PDF file using PdfiumViewer
                using (var document = PdfDocument.Load(pdfPath))
                {
                    // Get the number of pages in the PDF
                    int pageCount = document.PageCount;
                    Console.WriteLine($"PDF file has {pageCount} pages");

                    // Process each page
                    for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                    {
                        Console.WriteLine($"Processing page {pageIndex + 1}");

                        // Get page dimensions
                        var pageSize = document.PageSizes[pageIndex];

                        // Calculate image dimensions
                        int width = (int)(pageSize.Width * scale);
                        int height = (int)(pageSize.Height * scale);

                        // Set rendering flags
                        var renderFlags = PdfRenderFlags.Annotations |
                                          PdfRenderFlags.CorrectFromDpi |
                                          PdfRenderFlags.ForPrinting |    // Use print mode
                                          PdfRenderFlags.LcdText;         // Use LCD-optimized text rendering

                        // Render the page
                        using (var image = document.Render(pageIndex, width, height, dpi, dpi, renderFlags))
                        {
                            // Create a high-quality image
                            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                            {
                                using (var g = Graphics.FromImage(bitmap))
                                {
                                    // Set high-quality drawing options
                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                                    // Draw a white background (ensures form elements are clearly visible)
                                    g.Clear(Color.White);

                                    // Draw the rendered PDF page
                                    g.DrawImage(image, 0, 0, width, height);
                                }

                                // Save as PNG
                                string outputPath = Path.Combine(outputFolder, $"page-{pageIndex + 1}.png");

                                // Use high-quality settings for saving
                                var encoderParameters = new EncoderParameters(1);
                                encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Convert.ToByte(100));
                                bitmap.Save(outputPath, GetEncoder(ImageFormat.Png), encoderParameters);

                                Console.WriteLine($"Saved: {outputPath}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during conversion process: {ex.Message}");
                throw;
            }
        }

        // Get the encoder for the specified image format
        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}
