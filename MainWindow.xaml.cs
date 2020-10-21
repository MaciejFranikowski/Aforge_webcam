using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Accord.Video.FFMPEG;
using AForge.Video;
using AForge.Video.DirectShow;
using AForge.Vision.Motion;
using Microsoft.Win32;

namespace AForge_webcam
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Public properties

        public ObservableCollection<FilterInfo> VideoDevices { get; set; }
        public ObservableCollection<System.Drawing.Size> VideoResolutions { get; set; }
       
        public FilterInfo CurrentDevice
        {
            get { return _currentDevice; }
            set { _currentDevice = value; this.OnPropertyChanged("CurrentDevice"); }
        }
        private FilterInfo _currentDevice;

        public System.Drawing.Size CurrentResolution {
            get { return _currentResolution; }
            set { _currentResolution = value; this.OnPropertyChanged("CurrentResolution"); }
        }
        private System.Drawing.Size _currentResolution;


        #endregion

        #region Private fields

        private MotionDetector detector;
        private VideoCaptureDevice _videoSource;
        private VideoFileWriter _writer;
        private bool _recording;
        private DateTime? _firstFrameTime = null;
        private BitmapImage bi;
        
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            detector = new MotionDetector(new SimpleBackgroundModelingDetector(), new MotionAreaHighlighting());
            this.DataContext = this;
            GetVideoDevices();
            getVideoResolution();
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            StopCamera();
        }

        private void StopCamera()
        {
            if (_videoSource != null && _videoSource.IsRunning)
            {
                _videoSource.SignalToStop();
                _videoSource.NewFrame -= new NewFrameEventHandler(video_NewFrame);
            }
            videoPlayer.Source = null;
            Dispatcher.BeginInvoke(new ThreadStart(delegate { motionTextBox.Text = ""; }));
        }

        private void video_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            
            try
            {
                if (_recording)
                {
                    recordVideo(eventArgs);
                }

                
                using (var bitmap = (Bitmap)eventArgs.Frame.Clone())
                {
                    bi = bitmap.ToBitmapImage();
                }
                bi.Freeze(); // avoid cross thread operations and prevents leaks
                Dispatcher.BeginInvoke(new ThreadStart(delegate{ videoPlayer.Source= bi;}));

                motionDetect(eventArgs);
                
            }
            catch (Exception exc)
            {
                MessageBox.Show("Error on _videoSource_NewFrame:\n" + exc.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StopCamera();
            }
        }

        private void motionDetect(NewFrameEventArgs eventArgs)
        {
            try
            {
                using (var bitmap = (Bitmap)eventArgs.Frame.Clone())
                {
                    if (detector.ProcessFrame(bitmap) > 0.02)
                    {
                        
                        Dispatcher.BeginInvoke(new ThreadStart(delegate { motionTextBox.Text = "Motion detected"; }));
                        
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(new ThreadStart(delegate { motionTextBox.Text = "Motion not detected"; }));
                    }
                }
            }
            catch (Exception exc)
            {
                MessageBox.Show("Error on _videoSource_NewFrame:\n" + exc.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                //StopCamera();
            }
        }

        private void recordVideo(NewFrameEventArgs eventArgs) 
        {
            using (var bitmap = (Bitmap)eventArgs.Frame.Clone())
            {
                try
                {
                    if (_firstFrameTime != null)
                    {
                        _writer.WriteVideoFrame(bitmap, DateTime.Now - _firstFrameTime.Value);
                    }
                    else
                    {
                        _writer.WriteVideoFrame(bitmap);
                        _firstFrameTime = DateTime.Now;
                    }
                }
                catch (Exception exc)
                {
                    MessageBox.Show("Error on _videoSource_NewFrame:\n" + exc.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StopCamera();
                }
            }
        }

        private void GetVideoDevices()
        {
            VideoDevices = new ObservableCollection<FilterInfo>();
            foreach (FilterInfo filterInfo in new FilterInfoCollection(FilterCategory.VideoInputDevice))
            {
                VideoDevices.Add(filterInfo);
            }

            if (VideoDevices.Any()) 
            {
                CurrentDevice = VideoDevices[0];
            }
            else
            {
                MessageBox.Show("No video sources were found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }


        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            StartCamera();
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
        }

        private void btnStopRec_Click(object sender, RoutedEventArgs e)
        {
            _recording = false;
            _writer.Close();
            _writer.Dispose();
        }

        private void btnStartRec_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog();
            dialog.FileName = "Video1";
            dialog.DefaultExt = ".avi";
            dialog.AddExtension = true;
            var dialogresult = dialog.ShowDialog();
            if (dialogresult != true)
            {
                return;
            }
            _firstFrameTime = null;
            _writer = new VideoFileWriter();
            _writer.Open(dialog.FileName, (int)Math.Round(bi.Width, 0), (int)Math.Round(bi.Height, 0));
            
            _recording = true;
        }

        private void btnSnap_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog();
            dialog.FileName = "Snapshot1";
            dialog.DefaultExt = ".png";
            var dialogresult = dialog.ShowDialog();
            if (dialogresult != true)
            {
                return;
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bi));
            using (var filestream = new FileStream(dialog.FileName, FileMode.Create))
            {
                encoder.Save(filestream);
            }
        }

        private void StartCamera()
        {
            if (CurrentDevice != null)
            {
                _videoSource = new VideoCaptureDevice(CurrentDevice.MonikerString);
                setVideoResolution();
                _videoSource.NewFrame += video_NewFrame;
                _videoSource.Start();
            }
        }

        private void getVideoResolution()
        {
            _videoSource = new VideoCaptureDevice(CurrentDevice.MonikerString);
            VideoResolutions = new ObservableCollection<System.Drawing.Size>() ;
            for (int i = 0; i < _videoSource.VideoCapabilities.Length; i++)
            {
                VideoResolutions.Add(_videoSource.VideoCapabilities[i].FrameSize);
            }
            if (VideoResolutions.Any())
            {
                _currentResolution = VideoResolutions[0];
            }
            else
            {
                MessageBox.Show("No video sources were found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }

        private void setVideoResolution()
        {
            for (int i = 0; i < _videoSource.VideoCapabilities.Length; i++)
            {
                if (_currentResolution.Equals(_videoSource.VideoCapabilities[i].FrameSize)){
                    _videoSource.VideoResolution = _videoSource.VideoCapabilities[i];
                }
            }
            
        }

        #region INotifyPropertyChanged members

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
            {
                var e = new PropertyChangedEventArgs(propertyName);
                handler(this, e);
            }
        }
        #endregion
    }
}
