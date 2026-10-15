using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>
    /// Watches the shop folders and calls back on the UI thread once changes settle (900 ms).
    /// </summary>
    internal sealed class ShopWatcher : IDisposable
    {
        readonly Control _owner;
        readonly Timer _debounce = new Timer();
        readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

        public ShopWatcher(Control owner, Action onSettled)
        {
            _owner = owner;
            _debounce.Interval = 900;
            _debounce.Tick += delegate { _debounce.Stop(); onSettled(); };
        }

        public void Start(string kbRoot)
        {
            foreach (string dir in ShopWatch.Dirs(kbRoot, ShopSync.LoadSettings()))
                WatchDir(dir);
        }

        public void Restart(string kbRoot)
        {
            StopWatchers();
            Start(kbRoot);
        }

        public void Dispose()
        {
            _debounce.Stop();
            _debounce.Dispose();
            StopWatchers();
        }

        void StopWatchers()
        {
            foreach (FileSystemWatcher w in _watchers)
            {
                try { w.Dispose(); }
                catch { }
            }
            _watchers.Clear();
        }

        void WatchDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var w = new FileSystemWatcher(dir);
                w.IncludeSubdirectories = true;
                w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size;
                w.Changed += delegate { Ping(); };
                w.Created += delegate { Ping(); };
                w.Deleted += delegate { Ping(); };
                w.Renamed += delegate { Ping(); };
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch { }
        }

        void Ping()
        {
            try
            {
                if (_owner.IsHandleCreated && !_owner.IsDisposed)
                    _owner.BeginInvoke(new Action(OnShopChanged));
            }
            catch { }
        }

        void OnShopChanged()
        {
            _debounce.Stop();
            _debounce.Start();
        }
    }
}
