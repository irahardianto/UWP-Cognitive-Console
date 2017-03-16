using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace UWP_Cognitive_Console
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        MediaCapture mediaCapture;
        FaceDetectionEffect faceDetectionEffect;
        public bool isDetectionProcessing { get; set; }
        public bool isFaceDetected { get; set; }
        public double CameraAspectRatio { get; set; }
        public int CameraResolutionWidth { get; private set; }
        public int CameraResolutionHeight { get; private set; }
        public int? LastDetectedVisitorId { get; set; }

        CoreDispatcher dispatcher = Windows.UI.Core.CoreWindow.GetForCurrentThread().Dispatcher;
        ImageAnalyzer imageAnalyzer = new ImageAnalyzer();

        public MainPage()
        {
            this.InitializeComponent();

            ApplicationView.PreferredLaunchViewSize = new Size(800, 360);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;

            isDetectionProcessing = false;
            isFaceDetected = false;
            
            this.textBox.TextChanged += TextBox_TextChanged;

            InitializeCamera();
        }

        private async void button_Click(object sender, RoutedEventArgs e)
        {
            textBox.Text += NetworkInterface.GetIsNetworkAvailable() == true ? "Internet connected successfully \n" : "Cannot connect to the internet \n";
            textBox.Text += "Running detection loop \n";

            while (true)
            {
                if (!isDetectionProcessing)
                {
                    try
                    { 
                        imageAnalyzer.CheckRunningDwellTime();

                        await Task.WhenAll(DetectionLoop());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }
            }
        }

        public async void InitializeCamera()
        {
            textBox.Text += "Initializing Camera \n"; 

            try
            {
                if (mediaCapture != null)
                {
                    mediaCapture.Dispose();
                    mediaCapture = null;
                }

                mediaCapture = new MediaCapture();
                await mediaCapture.InitializeAsync();

                // Set callbacks for failure and recording limit exceeded
                mediaCapture.Failed += new MediaCaptureFailedEventHandler(mediaCapture_Failed);
                mediaCapture.RecordLimitationExceeded += new Windows.Media.Capture.RecordLimitationExceededEventHandler(mediaCapture_RecordLimitExceeded);

                CaptureElement preview = new CaptureElement();
                preview.Source = mediaCapture;
                await mediaCapture.StartPreviewAsync();

                SetVideoEncodingToHighestResolution();

                if (faceDetectionEffect == null || !faceDetectionEffect.Enabled)
                {
                    await CreateFaceDetectionEffectAsync();
                }
                else
                {
                    //await CleanUpFaceDetectionEffectAsync();
                }

                textBox.Text += "Initializing Camera Success \n";
            }
            catch (Exception ex)
            {
                textBox.Text += $"Initializing Camera Failed { ex.Message } \n";
            }
        }

        public async Task DetectionLoop()
        {
            Debug.WriteLine("Running Detection Loop");
            isDetectionProcessing = true;
            var startTime = DateTime.Now;

            var photoFile = await DownloadsFolder.CreateFileAsync("tmp", CreationCollisionOption.GenerateUniqueName);
            ImageEncodingProperties imageProperties = ImageEncodingProperties.CreateJpeg();

            await mediaCapture.CapturePhotoToStorageFileAsync(imageProperties, photoFile);
            IRandomAccessStream stream = await photoFile.OpenReadAsync();
            var imageStream = ReadFully(stream.AsStream());

            if (isFaceDetected)
            {
                isFaceDetected = false;

                Debug.WriteLine("Processing Detection");

                await Task.WhenAll(imageAnalyzer.DetectFaces(imageStream), imageAnalyzer.DetectEmotion(imageStream));
                await Task.WhenAll(imageAnalyzer.FindSimilarFace());
                await Task.WhenAll(imageAnalyzer.MapFaceIdEmotionResult());

                TimeSpan timeDifference = DateTime.Now - startTime;

                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => {

                    foreach (var person in imageAnalyzer.CurrentPerson)
                    {
                        var faceId = person.VisitorId;
                        var age = person.Age.ToString();
                        var gender = person.Gender;
                        var emotion = person.HighestEmotion;
                        var dwell = imageAnalyzer.GetDwellTime(person.VisitorId);

                        textBox.Text += $"[ { DateTime.Now } ] - {((int)timeDifference.Milliseconds).ToString()}ms - face id : { faceId } | age : { age } | sex : { gender } | emotion : { emotion } | dwell : { dwell } \n";
                    }
                });
            }
            isDetectionProcessing = false;
        }

        public byte[] ReadFully(Stream input)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.scrollViewer.ChangeView(0.0f, this.scrollViewer.ExtentHeight, 1.0f);
        }

        private async Task CreateFaceDetectionEffectAsync()
        {
            // Create the definition, which will contain some initialization settings
            var definition = new FaceDetectionEffectDefinition();

            // To ensure preview smoothness, do not delay incoming samples
            definition.SynchronousDetectionEnabled = false;

            // In this scenario, choose detection speed over accuracy
            definition.DetectionMode = FaceDetectionMode.HighPerformance;

            // Add the effect to the preview stream
            faceDetectionEffect = (FaceDetectionEffect)await mediaCapture.AddVideoEffectAsync(definition, MediaStreamType.VideoPreview);

            // Register for face detection events
            faceDetectionEffect.FaceDetected += FaceDetectionEffect_FaceDetected;

            // Choose the shortest interval between detection events
            faceDetectionEffect.DesiredDetectionInterval = TimeSpan.FromMilliseconds(33);

            // Start detecting faces
            faceDetectionEffect.Enabled = true;
        }

        private async void FaceDetectionEffect_FaceDetected(FaceDetectionEffect sender, FaceDetectedEventArgs args)
        {
            isFaceDetected = args.ResultFrame.DetectedFaces.Count > 0 ? true : false;
        }

        private async void SetVideoEncodingToHighestResolution()
        {
            VideoEncodingProperties highestVideoEncodingSetting;
            uint maxHeightForRealTime = 720;
            highestVideoEncodingSetting = mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoPreview).Cast<VideoEncodingProperties>().Where(v => v.Height <= maxHeightForRealTime).OrderByDescending(v => v.Width * v.Height * (v.FrameRate.Numerator / v.FrameRate.Denominator)).First();

            if (highestVideoEncodingSetting != null)
            {
                this.CameraAspectRatio = (double)highestVideoEncodingSetting.Width / (double)highestVideoEncodingSetting.Height;
                this.CameraResolutionHeight = (int)highestVideoEncodingSetting.Height;
                this.CameraResolutionWidth = (int)highestVideoEncodingSetting.Width;

                await mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(MediaStreamType.VideoPreview, highestVideoEncodingSetting);
            }
        }

        private void mediaCapture_Failed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs)
        {
            throw new NotImplementedException();
        }

        private void mediaCapture_RecordLimitExceeded(MediaCapture sender)
        {
            throw new NotImplementedException();
        }
    }
}
