using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SSRename
{
    internal sealed class SettingsSaveCoordinator : IDisposable
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

        private readonly object lifecycleSync = new object();
        private readonly object saveSync = new object();
        private readonly Func<PluginSettings> snapshotSettings;
        private readonly Action<PluginSettings> saveSettings;
        private readonly TimeSpan interval;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private byte[] lastSavedPayload;
        private Task periodicTask;
        private Task<bool> stopTask;
        private bool started;
        private bool stopped;

        public event Action<Exception> SaveFailed;

        public SettingsSaveCoordinator(
            PluginSettings lastSavedSettings,
            Func<PluginSettings> snapshotSettings,
            Action<PluginSettings> saveSettings,
            TimeSpan? interval = null)
        {
            if (lastSavedSettings == null) throw new ArgumentNullException("lastSavedSettings");
            this.snapshotSettings = snapshotSettings ?? throw new ArgumentNullException("snapshotSettings");
            this.saveSettings = saveSettings ?? throw new ArgumentNullException("saveSettings");
            this.interval = interval ?? DefaultInterval;
            if (this.interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("interval");
            lastSavedPayload = lastSavedSettings.SerializeUtf8();
        }

        public static SettingsSaveCoordinator ForFile(
            string path,
            PluginSettings lastSavedSettings,
            Func<PluginSettings> snapshotSettings,
            TimeSpan? interval = null)
        {
            return new SettingsSaveCoordinator(lastSavedSettings, snapshotSettings,
                delegate(PluginSettings value) { value.SaveAtomic(path); }, interval);
        }

        public void Start()
        {
            lock (lifecycleSync)
            {
                if (stopped) throw new ObjectDisposedException("SettingsSaveCoordinator");
                if (started) return;
                started = true;
                periodicTask = Task.Run((Func<Task>)RunPeriodicAsync);
            }
        }

        private async Task RunPeriodicAsync()
        {
            var token = cancellation.Token;
            var clock = Stopwatch.StartNew();
            var nextDueTicks = interval.Ticks;
            while (true)
            {
                var remaining = TimeSpan.FromTicks(nextDueTicks - clock.Elapsed.Ticks);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                SaveIfChanged();

                nextDueTicks += interval.Ticks;
                if (nextDueTicks <= clock.Elapsed.Ticks)
                    nextDueTicks = clock.Elapsed.Ticks + interval.Ticks;
            }
        }

        private bool SaveIfChanged()
        {
            lock (saveSync)
            {
                PluginSettings snapshot;
                byte[] payload;
                try
                {
                    snapshot = snapshotSettings();
                    if (snapshot == null) throw new InvalidOperationException("The settings snapshot was null.");
                    snapshot = snapshot.Clone();
                    payload = snapshot.SerializeUtf8();
                }
                catch (Exception ex)
                {
                    NotifySaveFailed(ex);
                    return false;
                }

                if (BytesEqual(lastSavedPayload, payload))
                    return true;

                try
                {
                    saveSettings(snapshot);
                    lastSavedPayload = payload;
                    return true;
                }
                catch (Exception ex)
                {
                    NotifySaveFailed(ex);
                    return false;
                }
            }
        }

        private void NotifySaveFailed(Exception exception)
        {
            var handler = SaveFailed;
            if (handler == null) return;
            try { handler(exception); }
            catch { }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        public bool StopAndFlush()
        {
            Task<bool> task;
            lock (lifecycleSync)
            {
                if (stopTask == null)
                {
                    stopped = true;
                    cancellation.Cancel();
                    stopTask = Task.Run((Func<bool>)StopCore);
                }
                task = stopTask;
            }
            return task.GetAwaiter().GetResult();
        }

        private bool StopCore()
        {
            Task periodic;
            lock (lifecycleSync) periodic = periodicTask;
            if (periodic != null)
            {
                try { periodic.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { }
            }
            return SaveIfChanged();
        }

        public void Dispose()
        {
            StopAndFlush();
            cancellation.Dispose();
        }
    }
}
