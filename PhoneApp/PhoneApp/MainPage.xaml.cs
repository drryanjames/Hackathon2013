﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using PhoneApp.Resources;

// Directives
using System.ComponentModel;
using System.Threading;
using System.IO;
using System.IO.IsolatedStorage;
using Microsoft.Devices;
using System.Windows.Media;
using System.Text;
using System.Windows.Media.Imaging;
using System.Windows.Threading;





namespace PhoneApp
{
    public partial class MainPage : PhoneApplicationPage
    {
        // Viewfinder for capturing video.
        private VideoBrush videoRecorderBrush;

        // Source and device for capturing video.
        private CaptureSource captureSource;
        private VideoCaptureDevice videoCaptureDevice;

        // File details for storing the recording.        
        private IsolatedStorageFileStream isoVideoFile;
        private FileSink fileSink;
        private string isoVideoFileName = "CameraMovie.mp4";

        // For managing button and application state.
        private enum ButtonState { Initialized, Ready, Recording, Playback, Paused, NoChange, CameraNotSupported };
        private ButtonState currentAppState;
        private Thread picCapturer;
        private bool capturingScreenshots;
        private ThreadStart start;

        // connection info for sending to server
        static bool debug = false;
        private static string domain = debug ? "169.254.80.80" : "glimpse.cloudapp.net";
        private static int port = 4242;

        // creating timer instance
        DispatcherTimer newTimer = new DispatcherTimer();

        // Constructor
        public MainPage()
        {
            InitializeComponent();

            // Prepare ApplicationBar and buttons.
            PhoneAppBar = (ApplicationBar)ApplicationBar;
            PhoneAppBar.IsVisible = true;
            StartRecording = ((ApplicationBarIconButton)ApplicationBar.Buttons[0]);
            StopPlaybackRecording = ((ApplicationBarIconButton)ApplicationBar.Buttons[1]);
            StartPlayback = ((ApplicationBarIconButton)ApplicationBar.Buttons[2]);
            PausePlayback = ((ApplicationBarIconButton)ApplicationBar.Buttons[3]);                 
        }

        // If recording fails, display an error message.
        private void OnCaptureFailed(object sender, ExceptionRoutedEventArgs e)
        {
            this.Dispatcher.BeginInvoke(delegate()
            {
                txtDebug.Text = "ERROR: " + e.ErrorException.Message.ToString();
            });
        }

        // Display the viewfinder when playback ends.
        public void VideoPlayerMediaEnded(object sender, RoutedEventArgs e)
        {
            // Remove the playback objects.
            DisposeVideoPlayer();

            StartVideoPreview();
        }


        private void DisposeVideoPlayer()
        {
            if (VideoPlayer != null)
            {
                // Stop the VideoPlayer MediaElement.
                VideoPlayer.Stop();

                // Remove playback objects.
                VideoPlayer.Source = null;
                isoVideoFile = null;

                // Remove the event handler.
                VideoPlayer.MediaEnded -= VideoPlayerMediaEnded;
            }
        }

        private void DisposeVideoRecorder()
        {
            if (captureSource != null)
            {
                // Stop captureSource if it is running.
                if (captureSource.VideoCaptureDevice != null
                    && captureSource.State == CaptureState.Started)
                {
                    captureSource.Stop();
                }

                // Remove the event handler for captureSource.
                captureSource.CaptureFailed -= OnCaptureFailed;

                // Remove the video recording objects.
                captureSource = null;
                videoCaptureDevice = null;
                fileSink = null;
                videoRecorderBrush = null;
            }
        }

        // Set recording state: start recording.
        private void StartVideoRecording()
        {
            try
            {
                // Connect fileSink to captureSource.
                if (captureSource.VideoCaptureDevice != null
                    && captureSource.State == CaptureState.Started)
                {
                    captureSource.Stop();
                    
                    // Connect the input and output of fileSink.
                    fileSink.CaptureSource = captureSource;
                    fileSink.IsolatedStorageFileName = isoVideoFileName;
                }

                // Begin recording.
                if (captureSource.VideoCaptureDevice != null
                    && captureSource.State == CaptureState.Stopped)
                {
                    captureSource.Start();
                    
                    //Variables for image capturing
                    start = delegate { CaptureVideoPics(captureSource); };
                    picCapturer = new Thread(start);
                    Dispatcher.BeginInvoke(() => CaptureVideoPics(captureSource));
                }

                // Set the button states and the message.
                UpdateUI(ButtonState.Recording, "Recording...");
            }

            // If recording fails, display an error.
            catch (Exception e)
            {
                this.Dispatcher.BeginInvoke(delegate()
                {
                    txtDebug.Text = "ERROR: " + e.Message.ToString();
                });
            }
        }

        private void CaptureVideoPics(CaptureSource source)
        {
            // timer interval specified as 1 second
            var fps = 24;
            newTimer.Interval = TimeSpan.FromMilliseconds(1000 / fps); // 24 times a second
            // Sub-routine OnTimerTick will be called at every second
            newTimer.Tick += (sender, args) => 
            {                
                if (!capturingScreenshots)
                {
                    capturingScreenshots = true;
                    source.CaptureImageAsync();
                }
            };
            // starting the timer
            newTimer.Start();
            captureSource.CaptureImageCompleted += new EventHandler<CaptureImageCompletedEventArgs>(CaptureSource_CaptureImageCompleted);
            captureSource.CaptureFailed += new EventHandler<ExceptionRoutedEventArgs>(CaptureSource_CaptureFailed);
        }

        private int quality = 15;
        void CaptureSource_CaptureImageCompleted(object sender, CaptureImageCompletedEventArgs e)
        {
            ImageBrush capturedImage = new ImageBrush();
            WriteableBitmap map = e.Result;
            var targetStream = new MemoryStream();
            map.SaveJpeg(targetStream, 640, 480, 0, quality);
            
            CaptureSource s = (CaptureSource)sender;

            // convert stream to string
            targetStream.Seek(0, SeekOrigin.Begin);
            string imageStream = Convert.ToBase64String(targetStream.ToArray());

            string postBody = string.Format(@"{{""img"":""{0}""}}", imageStream);

            string url = string.Format("http://{0}:{1}/image", domain, port);

            Post(url, postBody, webResponseCallback);

            capturingScreenshots = false;
        }

        void CaptureSource_CaptureFailed(object sender, ExceptionRoutedEventArgs e)
        {
            throw new Exception( String.Format("Error capturing the image:{0}",e.ErrorException)); 
        }

        // Set the recording state: stop recording.
        private void StopVideoRecording()
        {
            try
            {
                // Stop recording.
                if (captureSource.VideoCaptureDevice != null
                && captureSource.State == CaptureState.Started)
                {
                    // stop the timer
                    newTimer.Stop();

                    // send stop to the server
                    string url = string.Format("http://{0}:{1}/stop", domain, port);

                    string postBody = @"{""ignored"":""post body is ignored, just putting something to reuse same post function""}";

                    Post(url, postBody, webResponseCallback);

                    captureSource.Stop();
                    // Disconnect fileSink.
                    fileSink.CaptureSource = null;
                    fileSink.IsolatedStorageFileName = null;

                    // Set the button states and the message.
                    UpdateUI(ButtonState.NoChange, "Preparing viewfinder...");

                    StartVideoPreview();
                }
            }
            // If stop fails, display an error.
            catch (Exception e)
            {
                this.Dispatcher.BeginInvoke(delegate()
                {
                    txtDebug.Text = "ERROR: " + e.Message.ToString();
                });
            }
        }

        // Start the video recording.
        private void StartRecording_Click(object sender, EventArgs e)
        {
            // Avoid duplicate taps.
            StartRecording.IsEnabled = false;

            StartVideoRecording();
        }

        // Handle stop requests.
        private void StopPlaybackRecording_Click(object sender, EventArgs e)
        {
            // Avoid duplicate taps.
            StopPlaybackRecording.IsEnabled = false;

            // Stop during video recording.
            if (currentAppState == ButtonState.Recording)
            {
                StopVideoRecording();

                // Set the button state and the message.
                UpdateUI(ButtonState.NoChange, "Recording stopped.");
            }

            // Stop during video playback.
            else
            {
                // Remove playback objects.
                DisposeVideoPlayer();

                StartVideoPreview();

                // Set the button state and the message.
                UpdateUI(ButtonState.NoChange, "Playback stopped.");
            }
        }

        // Start video playback.
        private void StartPlayback_Click(object sender, EventArgs e)
        {
            // Avoid duplicate taps.
            StartPlayback.IsEnabled = false;

            // Start video playback when the file stream exists.
            if (isoVideoFile != null)
            {
                VideoPlayer.Play();
            }
            // Start the video for the first time.
            else
            {
               

                // Stop the capture source.
                captureSource.Stop();

                // Remove VideoBrush from the tree.
                viewfinderRectangle.Fill = null;

                // Create the file stream and attach it to the MediaElement.
                isoVideoFile = new IsolatedStorageFileStream(isoVideoFileName,
                                        FileMode.Open, FileAccess.Read,
                                        IsolatedStorageFile.GetUserStoreForApplication());

                VideoPlayer.SetSource(isoVideoFile);

                // Add an event handler for the end of playback.
                VideoPlayer.MediaEnded += new RoutedEventHandler(VideoPlayerMediaEnded);

                // Start video playback.
                VideoPlayer.Play();
            }

            // Set the button state and the message.
            UpdateUI(ButtonState.Playback, "Playback started.");
        }

        // Pause video playback.
        private void PausePlayback_Click(object sender, EventArgs e)
        {
            // Avoid duplicate taps.
            PausePlayback.IsEnabled = false;

            // If mediaElement exists, pause playback.
            if (VideoPlayer != null)
            {
                VideoPlayer.Pause();
            }

            // Set the button state and the message.
            UpdateUI(ButtonState.Paused, "Playback paused.");
        }


        // Set the recording state: display the video on the viewfinder.
        private void StartVideoPreview()
        {
            try
            {
                // Display the video on the viewfinder.
                if (captureSource.VideoCaptureDevice != null
                && captureSource.State == CaptureState.Stopped)
                {
                    // Add captureSource to videoBrush.
                    videoRecorderBrush.SetSource(captureSource);

                    // Add videoBrush to the visual tree.
                    viewfinderRectangle.Fill = videoRecorderBrush;

                    captureSource.Start();

                    // Set the button states and the message.
                    UpdateUI(ButtonState.Ready, "Ready to record.");
                }
            }
            // If preview fails, display an error.
            catch (Exception e)
            {
                this.Dispatcher.BeginInvoke(delegate()
                {
                    txtDebug.Text = "ERROR: " + e.Message.ToString();
                });
            }
        }

        public void InitializeVideoRecorder()
        {
            if (captureSource == null)
            {
                // Create the VideoRecorder objects.
                captureSource = new CaptureSource();
                fileSink = new FileSink();

                videoCaptureDevice = CaptureDeviceConfiguration.GetDefaultVideoCaptureDevice();

                // Add eventhandlers for captureSource.
                captureSource.CaptureFailed += new EventHandler<ExceptionRoutedEventArgs>(OnCaptureFailed);

                // Initialize the camera if it exists on the phone.
                if (videoCaptureDevice != null)
                {
                    // Create the VideoBrush for the viewfinder.
                    videoRecorderBrush = new VideoBrush();
                    videoRecorderBrush.SetSource(captureSource);

                    // Display the viewfinder image on the rectangle.
                    viewfinderRectangle.Fill = videoRecorderBrush;

                    // Start video capture and display it on the viewfinder.
                    captureSource.Start();

                    // Set the button state and the message.
                    UpdateUI(ButtonState.Initialized, "Tap record to start recording...");
                }
                else
                {
                    // Disable buttons when the camera is not supported by the phone.
                    UpdateUI(ButtonState.CameraNotSupported, "A camera is not supported on this phone.");
                }
            }
        }


        // Update the buttons and text on the UI thread based on app state.
        private void UpdateUI(ButtonState currentButtonState, string statusMessage)
        {
            // Run code on the UI thread.
            Dispatcher.BeginInvoke(delegate
            {

                switch (currentButtonState)
                {
                    // When the camera is not supported by the phone.
                    case ButtonState.CameraNotSupported:
                        StartRecording.IsEnabled = false;
                        StopPlaybackRecording.IsEnabled = false;
                        StartPlayback.IsEnabled = false;
                        PausePlayback.IsEnabled = false;
                        break;

                    // First launch of the application, so no video is available.
                    case ButtonState.Initialized:
                        StartRecording.IsEnabled = true;
                        StopPlaybackRecording.IsEnabled = false;
                        StartPlayback.IsEnabled = false;
                        PausePlayback.IsEnabled = false;
                        break;

                    // Ready to record, so video is available for viewing.
                    case ButtonState.Ready:
                        StartRecording.IsEnabled = true;
                        StopPlaybackRecording.IsEnabled = false;
                        StartPlayback.IsEnabled = true;
                        PausePlayback.IsEnabled = false;
                        break;

                    // Video recording is in progress.
                    case ButtonState.Recording:
                        StartRecording.IsEnabled = false;
                        StopPlaybackRecording.IsEnabled = true;
                        StartPlayback.IsEnabled = false;
                        PausePlayback.IsEnabled = false;
                        break;

                    // Video playback is in progress.
                    case ButtonState.Playback:
                        StartRecording.IsEnabled = false;
                        StopPlaybackRecording.IsEnabled = true;
                        StartPlayback.IsEnabled = false;
                        PausePlayback.IsEnabled = true;
                        break;

                    // Video playback has been paused.
                    case ButtonState.Paused:
                        StartRecording.IsEnabled = false;
                        StopPlaybackRecording.IsEnabled = true;
                        StartPlayback.IsEnabled = true;
                        PausePlayback.IsEnabled = false;
                        break;

                    default:
                        break;
                }

                // Display a message.
                txtDebug.Text = statusMessage;

                // Note the current application state.
                currentAppState = currentButtonState;
            });
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Initialize the video recorder.
            InitializeVideoRecorder();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // Dispose of camera and media objects.
            DisposeVideoPlayer();
            DisposeVideoRecorder();

            base.OnNavigatedFrom(e);
        }

        private void webResponseCallback(string val)
        {
            // TODO: do something with response
            //   Note: This is the callback for all web requests
        }

        public void Post(string address, string parameters, Action<string> onResponseGot)
        {
            Uri uri = new Uri(address);
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(uri);
            r.Method = "POST";
            r.ContentType = "application/json";

            r.BeginGetRequestStream(delegate(IAsyncResult req)
            {
                var outStream = r.EndGetRequestStream(req);

                using (StreamWriter w = new StreamWriter(outStream))
                {
                    w.Write(parameters);
                }

                r.BeginGetResponse(delegate(IAsyncResult result)
                {
                    try
                    {
                        HttpWebResponse response = (HttpWebResponse)r.EndGetResponse(result);

                        using (var stream = response.GetResponseStream())
                        {
                            using (StreamReader reader = new StreamReader(stream))
                            {
                                onResponseGot(reader.ReadToEnd());
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        onResponseGot("ERROR: " + e.ToString());
                    }

                }, null);

            }, null);
        }
    }
}