﻿using GBCLV3.Models.Download;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace GBCLV3.Services.Download
{
    class DownloadService : IDisposable
    {
        #region Events

        public event Action<DownloadResult> Completed;

        public event Action<DownloadProgress> ProgressChanged;

        #endregion

        #region Private Fields

        private const int BUFFER_SIZE = 4096; // byte
        private const double INFO_UPDATE_INTERVAL = 1.0; // second

        private readonly HttpClient _client;
        private readonly DispatcherTimer _timer;
        private readonly CancellationTokenSource _cts;
        private readonly AutoResetEvent _sync;

        private List<DownloadItem> _downloadItems;

        private int _totalBytes;
        private int _downloadedBytes;
        private int _previousDownloadedBytes;
        private int _totalCount;
        private int _completedCount;
        private int _failledCount;

        #endregion

        #region Constructor

        public DownloadService(IEnumerable<DownloadItem> downloadItems)
        {
            _client = new HttpClient() { Timeout = TimeSpan.FromMinutes(3.0) };
            _client.DefaultRequestHeaders.Connection.Add("keep-alive");

            _cts = new CancellationTokenSource();
            _sync = new AutoResetEvent(true);

            _timer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromSeconds(INFO_UPDATE_INTERVAL)
            };

            // Update download progress and raise events
            _timer.Tick += (sender, e) => ProgressChanged?.Invoke(GetDownloadProgress());

            // Initialize states
            _downloadItems = downloadItems.ToList();
            _totalBytes = _downloadItems.Sum(item => item.Size);
            _downloadedBytes = 0;
            _previousDownloadedBytes = 0;

            _totalCount = _downloadItems.Count();
            _completedCount = 0;
            _failledCount = 0;
        }

        #endregion

        #region Public Methods

        public async ValueTask<bool> StartAsync()
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            for (;;)
            {
                _timer.Start();

                await Task.Factory.StartNew(() =>
                {
                    Parallel.ForEach(_downloadItems, options, (item, state) =>
                    {
                        DownloadTask(item);
                        // 所以啊...止まるんじゃねぇぞ！
                        if (_cts.IsCancellationRequested) state.Stop();
                    });
                }, TaskCreationOptions.LongRunning);

                _timer.Stop();
                // Ensure the last progress report is fired
                ProgressChanged?.Invoke(GetDownloadProgress());

                // Succeeded
                if (_completedCount == _totalCount)
                {
                    Completed?.Invoke(DownloadResult.Succeeded);
                    return true;
                }

                // Clean incomplete files
                foreach (var item in _downloadItems)
                {
                    if (!item.IsCompleted && File.Exists(item.Path))
                    {
                        File.Delete(item.Path);
                    }
                }

                if (_failledCount > 0 && !_cts.IsCancellationRequested)
                {
                    Completed?.Invoke(DownloadResult.Incomplete);
                }

                // Wait for retry or cancel
                _sync.WaitOne();

                // Canceled
                if (_cts.IsCancellationRequested)
                {

                    Completed?.Invoke(DownloadResult.Canceled);
                    return false;
                }
            }
        }

        /// <summary>
        /// If previous download task is not fully completed (error occurred on some items)
        /// </summary>
        public void Retry()
        {
            _downloadItems = _downloadItems.Where(item => !item.IsCompleted).ToList();
            _downloadItems.ForEach(item => item.DownloadedBytes = 0);
            _failledCount = 0;

            _sync.Set();
        }

        /// <summary>
        /// Cancel ongoing download task
        /// </summary>
        public void Cancel()
        {
            _cts.Cancel();
            _timer.Stop();
            _sync.Set();
        }

        public void Dispose()
        {
            _client.Dispose();
            _cts.Dispose();
            _sync.Dispose();
        }

        #endregion

        #region Private Methods

        private void DownloadTask(DownloadItem item)
        {
            // Make sure directory exists
            if (Path.IsPathRooted(item.Path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Path));
            }

            try
            {
                var response = _client.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, _cts.Token).Result;

                if (response.StatusCode == HttpStatusCode.Found)
                {
                    // Handle redirection
                    var redirection = response.Headers.Location;
                    response = _client.GetAsync(redirection, HttpCompletionOption.ResponseHeadersRead, _cts.Token).Result;
                }

                if (item.Size == 0)
                {
                    item.Size = (int)(response.Content.Headers.ContentLength ?? 0);
                    Interlocked.Add(ref _totalBytes, item.Size);
                }

                using var httpStream = response.Content.ReadAsStreamAsync().Result;
                using var fileStream = File.OpenWrite(item.Path);

                // Close the stream if download is canceled
                _cts.Token.Register(() => httpStream.Close());

                Span<byte> buffer = stackalloc byte[BUFFER_SIZE];
                int bytesReceived;

                while ((bytesReceived = httpStream.Read(buffer)) > 0)
                {
                    fileStream.Write(buffer.Slice(0, bytesReceived));
                    item.DownloadedBytes += bytesReceived;
                    Interlocked.Add(ref _downloadedBytes, bytesReceived);
                }

                // Download successful
                item.IsCompleted = true;
                Interlocked.Increment(ref _completedCount);

                response.Dispose();
                return;
            }
            catch (OperationCanceledException ex)
            {
                // You are the one who anceled me :D
                Debug.WriteLine(ex.ToString());
            }
            catch (AggregateException ex) when (ex.InnerException is HttpRequestException)
            {
                Debug.WriteLine(ex.InnerException.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            // If is not caused by cancellation, mark as failure
            if (!_cts.IsCancellationRequested)
            {
                Interlocked.Increment(ref _failledCount);
                Interlocked.Add(ref _downloadedBytes, -item.DownloadedBytes);
            }
        }

        private DownloadProgress GetDownloadProgress()
        {
            // Calculate speed
            int diffBytes = _downloadedBytes - _previousDownloadedBytes;
            _previousDownloadedBytes = _downloadedBytes;

            return new DownloadProgress
            {
                TotalCount = _totalCount,
                CompletedCount = _completedCount,
                FailedCount = _failledCount,
                TotalBytes = _totalBytes,
                DownloadedBytes = _downloadedBytes,
                Speed = diffBytes / INFO_UPDATE_INTERVAL,
            };
        }

        #endregion
    }
}
