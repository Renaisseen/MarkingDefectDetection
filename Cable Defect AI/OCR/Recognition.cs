using System;
using OpenCvSharp; //OpenCv wrapper for C#
using Tesseract; //Tesseract OCR model (eng only within the project)
using System.Drawing;
using OpenCvSharp.Extensions;
using Rect = OpenCvSharp.Rect;
using Mat = OpenCvSharp.Mat;
using Emgu.CV.CvEnum;
using Emgu.CV;
using Emgu.CV.Structure;
using Size = OpenCvSharp.Size;
using Point = OpenCvSharp.Point;
using System.Text.RegularExpressions;

namespace CableDefect.OCR
{
    public static class Recognition
    {
        #region Globals
        private static string _tesseractModelsPath = @"E:/FreeZone/KNU/Cable Defect AI detector/Cable Defect AI/Cable Defect AI/tessdata";
        //private static string _preprocessedImagesPath = @"E:/FreeZone/KNU/Cable Defect AI detector/Preprocessed";
        public enum RecognizeOptions
        {
            FullText = 0,
            BoundariesEachSymbol = 1
        }

        public enum PreprocessOptions
        {
            No = 0,
            Full = 1,
            InvertColorsOnly = 2
        }
        #endregion

        #region Preprocess image
        public static string InvertColors(string imgName)
        {
            var filePath = getFilePath(imgName);
            Mat inputImage = Cv2.ImRead(filePath);

            Mat invertedColorsImage = new Mat();
            Cv2.BitwiseNot(inputImage, invertedColorsImage);
            Cv2.ImWrite(getPreprocessedPath(imgName, "invertedColorsImage"), invertedColorsImage);

            var finalFilePath = getPreprocessedPath(imgName, "invertedColorsImage");
            return finalFilePath;
        }
        public static Mat InvertColors(Mat img)
        {
            //var filePath = getFilePath(imgName);
            //Mat inputImage = Cv2.ImRead(filePath);

            Mat invertedColorsImage = new Mat();
            Cv2.BitwiseNot(img, invertedColorsImage);
            //Cv2.ImWrite(getPreprocessedPath(imgName, "invertedColorsImage"), invertedColorsImage);

            //var finalFilePath = getPreprocessedPath(imgName, "invertedColorsImage");
            return invertedColorsImage;
        }

        public static string PreprocessImageEmgu(string imgName)
        {
            var filePath = getFilePath(imgName);
            using var img = Pix.LoadFromFile(filePath);

            var scaledImage = img.Scale(2, 2);
            img.Save(getPreprocessedPath(imgName, "TESS_" + "scaledImage"));
            var grayImage = scaledImage.ConvertRGBToGray();
            grayImage.Save(getPreprocessedPath(imgName, "TESS_" + "grayImage"));
            
            Image<Gray, byte> thresholdedImage = new Image<Gray, byte>(getPreprocessedPath(imgName, "TESS_" + "grayImage"));
            CvInvoke.Threshold(thresholdedImage, thresholdedImage, 50, 100, ThresholdType.Binary);
            thresholdedImage.Save(getPreprocessedPath(imgName, "CV_" + "thresholdedImage"));

            //var thresholdedPix = grayImage.BinarizeOtsuAdaptiveThreshold(16, 16, 0, 0, 1.0f);
            //thresholdedPix.Save(getPreprocessedPath(imgName, "TESS_" + "thresholdedPix"));

            return getPreprocessedPath(imgName, "CV_" + "thresholdedImage");
        }
        
        public static string PreprocessImage(string imgName, bool withContours = false)
        {
            var filePath = getFilePath(imgName);
            Mat inputImage = Cv2.ImRead(filePath);

            // Resize the image (optional)
            //Mat resizedImage = new Mat();
            //Cv2.Resize(inputImage, resizedImage, new OpenCvSharp.Size(800, 600)); // Adjust size as needed
            //Cv2.ImWrite(getPreprocessedPath(imgName, "resizedImage"), resizedImage); // Save the resized image

            Mat grayscaleImage = new Mat();
            Cv2.CvtColor(inputImage, grayscaleImage, ColorConversionCodes.BGR2GRAY);
            Cv2.ImWrite(getPreprocessedPath(imgName, "grayscaleImage"), grayscaleImage);

            //Adaptive thresholding to binarize the image
            Mat thresholdedImage = new Mat();
            Cv2.Threshold(grayscaleImage, thresholdedImage, 200, 255, ThresholdTypes.Binary);
            Cv2.ImWrite(getPreprocessedPath(imgName, "thresholdedImage"), thresholdedImage);


            //Morphological transformations
            Mat morphedImage = new Mat();
            Mat kernelClose = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(4, 4));
            Cv2.MorphologyEx(thresholdedImage, morphedImage, MorphTypes.Close, kernelClose);

            Cv2.ImWrite(getPreprocessedPath(imgName, "morphedImage"), morphedImage);


            Mat colorInvertedImage = new Mat();
            colorInvertedImage = InvertColors(morphedImage);
            Cv2.ImWrite(getPreprocessedPath(imgName, "colorInvertedImage"), colorInvertedImage);

            var finalFilePath = string.Empty;

            if (withContours)
            {
                //Mat inputImageForRect = new Mat();
                //Cv2.Resize(inputImage, inputImageForRect, new OpenCvSharp.Size(800, 600));
                Mat annotationImage = new Mat();
                Cv2.Resize(colorInvertedImage, annotationImage, new OpenCvSharp.Size(800, 800));
                // Find contours
                Point[][] contours;
                HierarchyIndex[] hierarchy;
                Cv2.FindContours(morphedImage, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                Scalar rectangleColor = new Scalar(255, 0, 0);
                int rectangleThickness = 3;

                using (var engine = new TesseractEngine(_tesseractModelsPath, "eng", EngineMode.Default))
                {
                    engine.DefaultPageSegMode = PageSegMode.SingleChar;

                    Console.WriteLine("Contours.count: " + contours.Count() + "\n");
                    for (int i = 0; i < contours.Length; i++)
                    {
                        Rect boundingRect = Cv2.BoundingRect(contours[i]);
                        double area = Cv2.ContourArea(contours[i]);

                        if (boundingRect.Width >= 20 && boundingRect.Height >= 10 && boundingRect.Height < 500) //area?
                        {
                            Cv2.Rectangle(annotationImage, boundingRect, rectangleColor, rectangleThickness);
                            // Extract region of interest
                            Mat roi = new Mat(colorInvertedImage, boundingRect);

                            // Convert the ROI to grayscale for OCR
                            //Mat roiGray = new Mat();
                            //Cv2.CvtColor(roi, roiGray, ColorConversionCodes.BGR2GRAY);

                            using (Bitmap bitmap = roi.ToBitmap())
                            {
                                using (var page = engine.Process(bitmap))
                                {
                                    string text = page.GetText().Trim();
                                    Console.WriteLine($"Text in ROI {i + 1}: {text}");
                                }
                            }
                            Cv2.ImWrite(getPreprocessedPath(imgName, $"Contour[{i + 1}]"), roi);
                        }
                        
                    }
                }

                Cv2.ImWrite(getPreprocessedPath(imgName, "annotatedImage"), annotationImage);
                annotationImage.Dispose();
                //finalFilePath = getPreprocessedPath(imgName, "colorInvertedImage");
            }
            else
            {
                finalFilePath = getPreprocessedPath(imgName, "colorInvertedImage");
            }



            // Perform aggressive erosion to remove small white dots
            //Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            //Mat erodedImage = new Mat();
            //Cv2.Erode(thresholdedImage, erodedImage, kernel);
            //Cv2.ImWrite(getPreprocessedPath(imgName, "erodedImage"), erodedImage);

            // Perform dilation to restore larger objects if too much erosion has occurred
            //Mat restoredImage = new Mat();
            //Cv2.Dilate(erodedImage, restoredImage, kernel);
            //Cv2.ImWrite(getPreprocessedPath(imgName, "restoredImage"), restoredImage);

            // Find contours in the image
            //OpenCvSharp.Point[][] contours;
            //HierarchyIndex[] hierarchy;
            //Cv2.FindContours(morphedImage, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxTC89L1);

            //// Iterate through each contour
            //for (int i = 0; i < contours.Length; i++)
            //{
            //    Rect boundingBox = Cv2.BoundingRect(contours[i]);

            //    Cv2.Rectangle(morphedImage, boundingBox, Scalar.Red, 2);
            //}
            //Cv2.ImWrite(getPreprocessedPath(imgName, "contoursOnly"), morphedImage);



            //var finalFilePath = getPreprocessedPath(imgName, "thresholdedImage");
            

            inputImage.Dispose();
            //resizedImage.Dispose();
            grayscaleImage.Dispose();
            //equalizedImage.Dispose();
            thresholdedImage.Dispose();
            //blurredImage.Dispose();
            morphedImage.Dispose();
            //erodedImage.Dispose();
            //restoredImage.Dispose();
            colorInvertedImage.Dispose();

            return finalFilePath;
        }
        #endregion

        #region Recognize
        public static RecognitionResult RecognizeSimple(string imgName, PreprocessOptions preprocessing = PreprocessOptions.No)
        {
            var filePath = string.Empty;
            switch (preprocessing)
            {
                case PreprocessOptions.No:
                    filePath = getFilePath(imgName);
                    break;
                case PreprocessOptions.Full:
                    filePath = PreprocessImage(imgName, true);
                    if(filePath == string.Empty)
                    {
                        return new RecognitionResult();
                    }
                    break;
                case PreprocessOptions.InvertColorsOnly:
                    filePath = InvertColors(imgName);
                    break;
                default:
                    break;
            }
            
            using (var engine = new TesseractEngine(_tesseractModelsPath, "eng", EngineMode.Default))
            {
                using (var image = Pix.LoadFromFile(filePath))
                {
                    using (var page = engine.Process(image))
                    {
                        var text = page.GetText();

                        var additionalComment = string.Format("Mean confidence: {0}", page.GetMeanConfidence()) + "\n" + string.Format("Text (GetText): {0}", text);
                        Console.WriteLine(additionalComment);
                        return new RecognitionResult(true, text, additionalComment);
                    }
                }
            }
        }
        public static RecognitionResult Recognize(string imgName, RecognizeOptions recognize, PreprocessOptions preprocessing = PreprocessOptions.Full)
        {
            var filePath = string.Empty;
            switch (preprocessing)
            {
                case PreprocessOptions.No:
                    filePath = getFilePath(imgName);
                    break;
                case PreprocessOptions.Full:
                    filePath = PreprocessImage(imgName, false);
                    if (filePath == string.Empty)
                    {
                        return new RecognitionResult();
                    }
                    break;
                case PreprocessOptions.InvertColorsOnly:
                    filePath = InvertColors(imgName);
                    break;
                default:
                    break;
            }
            if(recognize == RecognizeOptions.BoundariesEachSymbol)
            {
                using (var engine = new TesseractEngine(_tesseractModelsPath, "eng", EngineMode.Default))
                {
                    engine.DefaultPageSegMode = PageSegMode.SingleChar;
                    using (var image = Pix.LoadFromFile(filePath))
                    {
                        using (var page = engine.Process(image))
                        {
                            var text = page.GetText().Trim();
                            text = Regex.Replace(text, @"\t|\n|\r", " ");

                            var additionalComment = string.Format("Mean confidence: {0}", page.GetMeanConfidence()) + "\n" + string.Format("Text (GetText): {0}", text);
                            Console.WriteLine(additionalComment);
                            return new RecognitionResult(true, text, additionalComment);
                        }
                    }
                }
            }
            else if (recognize == RecognizeOptions.FullText)
            {
                using (var engine = new TesseractEngine(_tesseractModelsPath, "eng", EngineMode.Default))
                {
                    //engine.DefaultPageSegMode = PageSegMode.SingleLine;
                    using (var image = Pix.LoadFromFile(filePath))
                    {
                        using (var page = engine.Process(image))
                        {
                            var text = page.GetText().Trim();
                            text = Regex.Replace(text, @"\t|\n|\r", " ");

                            var additionalComment = string.Format("Mean confidence: {0}", page.GetMeanConfidence()) + "\n" + string.Format("Text (GetText): {0}", text);
                            Console.WriteLine(additionalComment);
                            return new RecognitionResult(true, text, additionalComment);
                        }
                    }
                }
            }
            else
            {
                return new RecognitionResult();
            }
        }
        #endregion

        #region File pathes formation methods
        private static string getFilePath(string imgName)
        {
            //return $@"E:\FreeZone\KNU\Cable Defect AI detector\Files\{imgName}";
            return $@"E:\FreeZone\KNU\Cable Defect AI detector\Cable Defect AI\Cable Defect AI\Files\{imgName}";
        }
        private static string getPreprocessedPath(string imgName, string process)
        {
            //return $@"E:\FreeZone\KNU\Cable Defect AI detector\Preprocessed\{imgName}_-_{process}.jpg";
            return $@"E:\FreeZone\KNU\Cable Defect AI detector\Cable Defect AI\Cable Defect AI\Preprocessed\{imgName}_-_{process}.jpg";
        }

        private static string getPreprocessedName(string imgName, string process)
        {
            return $@"{imgName}_-_{process}.jpg";
        }
        #endregion
    }
    #region Auxiliary classes
    public class RecognitionResult
    {
        public bool Result { get; set; }
        public string Text { get; set; }
        public string Comment { get; set; }
        public RecognitionResult()
        {
            Result = false;
            Text = string.Empty;
            Comment = string.Empty;
        }
        public RecognitionResult(bool result, string text, string comment = "")
        {
            this.Result = result;
            this.Text = text;
            this.Comment = comment;
        }
    }
    #endregion
}
