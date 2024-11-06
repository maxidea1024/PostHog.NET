﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PostHog.Model;
using PostHog.Request;

namespace PostHog.Flush
{
    internal class AsyncIntervalFlushHandler : IDisposable
    {
        /// <summary>
        /// Our servers only accept payloads smaller than 32KB
        /// </summary>
        private const int ActionMaxSize = 32 * 1024;

        /// <summary>
        /// Our servers only accept request smaller than 512KB we left 12kb as margin error
        /// </summary>
        private const int BatchMaxSize = 500 * 1024;

        private readonly string _apiKey;

        private readonly CancellationTokenSource _continue;

        private readonly TimeSpan _flushInterval;

        private readonly JsonSerializerOptions? _jsonSerializerOptions;

        private readonly int _maxBatchSize;

        private readonly int _maxQueueSize;

        private readonly ConcurrentQueue<BaseAction> _queue;

        private readonly IRequestHandler _requestHandler;

        private readonly SemaphoreSlim _semaphore;

        private readonly int _threads;

        private Timer? _timer;

        internal AsyncIntervalFlushHandler(
            IRequestHandler requestHandler,
            int maxQueueSize,
            int maxBatchSize,
            TimeSpan flushInterval,
            int threads,
            string apiKey,
            JsonSerializerOptions? jsonSerializerOptions = null)
        {
            _queue = new ConcurrentQueue<BaseAction>();
            _requestHandler = requestHandler;
            _maxQueueSize = maxQueueSize;
            _maxBatchSize = maxBatchSize;
            _continue = new CancellationTokenSource();
            _flushInterval = flushInterval;
            _threads = threads;
            _semaphore = new SemaphoreSlim(_threads, _threads);
            _apiKey = apiKey;
            _jsonSerializerOptions = jsonSerializerOptions;

            RunInterval();
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _semaphore?.Dispose();
            _continue?.Cancel();
        }

        public async Task FlushAsync()
        {
            await PerformFlush().ConfigureAwait(false);
            await WaitWorkersToBeReleased();
        }

        public void Process(BaseAction action)
        {
            // TODO: 단지 길이 제한을 위해서 json을 시리얼라이징하고 있다. 이 부분을 제거할수 있을까?
            action.Size = JsonSerializer.Serialize(action, _jsonSerializerOptions).Length;

            if (action.Size > ActionMaxSize)
            {
                return;
            }

            _queue.Enqueue(action);
            if (_queue.Count >= _maxQueueSize)
            {
                _ = PerformFlush();
            }
        }

        private async Task FlushImpl()
        {
            var current = new List<BaseAction>();
            var currentSize = 0;
            while (!_queue.IsEmpty && !_continue.Token.IsCancellationRequested)
            {
                do
                {
                    if (!_queue.TryDequeue(out var action))
                    {
                        break;
                    }

                    current.Add(action);
                    currentSize += action.Size;
                } while (!_queue.IsEmpty && current.Count < _maxBatchSize && !_continue.Token.IsCancellationRequested &&
                         currentSize < BatchMaxSize - ActionMaxSize);

                if (current.Count > 0)
                {
                    // we have a batch that we're trying to send
                    var batch = new Batch(current, _apiKey); //TODO _apiKey는 따로 설정하는게 좋을듯한데?

                    // make the request here
                    await _requestHandler.MakeRequest(batch);

                    // mark the current batch as null
                    current = new List<BaseAction>();
                    currentSize = 0;
                }
            }
        }

        private async void IntervalCallback(object state)
        {
            await PerformFlush();
        }

        private async Task PerformFlush()
        {
            if (!await _semaphore.WaitAsync(1))
            {
                return;
            }

            try
            {
                await FlushImpl();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private void RunInterval()
        {
            var initialDelay = _queue.Count == 0 ? _flushInterval : TimeSpan.Zero;
            _timer = new Timer(IntervalCallback, new { }, initialDelay, _flushInterval);
        }

        private async Task WaitWorkersToBeReleased()
        {
            //for (var i = 0; i < _threads; i++) _semaphore.WaitOne();
            //await _semaphore.Wait(_threads);


            // TODO: 메모리 할당을 제거할 수 없나?

            List<Task> tasks = new List<Task>();
            for (int i = 0; i < _threads; i++)
            {
                tasks.Add(_semaphore.WaitAsync());
            }
            await Task.WhenAll(tasks.ToArray());

            _semaphore.Release(_threads);
        }
    }
}
