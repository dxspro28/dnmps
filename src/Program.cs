using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace dnmps {

    public static class Bass {
        [DllImport("bass")]
        public static extern int BASS_Init(int dev, int freq, int flags, IntPtr win, IntPtr dsguid);
        [DllImport("bass")]
        public static extern int BASS_StreamCreateFile(bool mem, string path, int offset, int len, int flags);
        [DllImport("bass")]
        public static extern int BASS_ChannelPlay(int handle, bool restart);
        [DllImport("bass")]
        public static extern int BASS_ChannelPause(int handle);
        [DllImport("bass")]
        public static extern int BASS_ChannelStop(int handle);
        [DllImport("bass")]
        public static extern int BASS_ChannelIsActive(int handle);
        [DllImport("bass")]
        public static extern int BASS_Free();
        [DllImport("bass")]
        public static extern int BASS_StreamFree(int handle);
        [DllImport("bass")]
        public static extern int BASS_ChannelFree(int handle);
        [DllImport("bass")]
        public static extern double BASS_ChannelBytes2Seconds(int handle, long pos);
        [DllImport("bass")]
        public static extern long BASS_ChannelSeconds2Bytes(int handle, double pos);
        [DllImport("bass")]
        unsafe public static extern int BASS_ChannelGetAttribute(int handle, int attrib, float *val);
        [DllImport("bass")]
        public static extern int BASS_ChannelSetAttribute(int handle, int attrib, float val);
        [DllImport("bass")]
        public static extern long BASS_ChannelGetLength(int handle, int mode);
        [DllImport("bass")]
        public static extern long BASS_ChannelGetPosition(int handle, int mode);
        [DllImport("bass")]
        public static extern int BASS_ChannelSetPosition(int handle, long pos, int mode);
        [DllImport("bass")]
        public static extern string BASS_ChannelGetTags(int handle, int tags);
    }

    public class MusicPlayerException : Exception {
        public MusicPlayerException(string err) : base(err) {}
    }

    public class MusicPlayer : IDisposable {

        List<string> playlist = null;
        int pl_index = 0;
        int handle;
        public event EventHandler<EventArgs> OnPlaylistFinished;
        float lastVolume = 1.0f;
        public bool Loading {
            get;
            private set;
        }
        public MusicPlayer() {
            if (Bass.BASS_Init(-1, 44100, 0, IntPtr.Zero, IntPtr.Zero) == 0) throw new MusicPlayerException("Failed to initialize device");
            this.playlist = new List<string>();
            this.OnPlaylistFinished += (sender, args) => { };
        }

        public void Dispose() {
            Bass.BASS_ChannelFree(this.handle);
            Bass.BASS_Free();
            this.playlist = null;
        }

        public void AddFile(string path) {
            this.playlist.Add(path);
        }

        public void AddFiles(string[] files) {
            foreach (var f in files) this.AddFile(f);
        }

        public void ShufflePlaylist() {
            var pl = this.playlist;
            List<string> newpl = new List<string>();
            var r = new Random();
            while (pl.Count > 0) {
                int idx = r.Next(pl.Count);
                if (newpl.Contains(pl[idx])) continue;
                newpl.Add(pl[idx]);
                pl.RemoveAt(idx);
                pl.TrimExcess();
            }
            this.playlist = newpl;
        }

        public string GetCurrentSong() {
            string current = this.playlist[this.pl_index];
            current = current.Substring(current.LastIndexOf('/') + 1);
            return current;
        }

        public bool Play() {
            this.Loading = true;
            string path = this.playlist[this.pl_index];
            this.handle = Bass.BASS_StreamCreateFile(false, path, 0, 0, 0);
            if(this.handle == 0) {
                this.Loading = false;
                return false;
            }
            if (Bass.BASS_ChannelPlay(this.handle, false) == 0) {
                this.Loading = false;
                return false;
            }
            this.Loading = false;
            this.SetVolume(this.lastVolume);
            return true;
        }

        public void Stop() {
            Bass.BASS_ChannelStop(this.handle);
        }

        public void Pause() {
            if(this.IsPlaying()) Bass.BASS_ChannelPause(this.handle);
        }

        public void Resume() {
            if(this.IsPaused()) Bass.BASS_ChannelPlay(this.handle, false);
        }

        public bool IsPaused() {
            return Bass.BASS_ChannelIsActive(this.handle) == 3;
        }

        public bool IsPlaying() {
            return Bass.BASS_ChannelIsActive(this.handle) == 1;
        }

        private bool CheckIndex(int idx) {
            return idx >= 0 && idx < this.playlist.Count;
        }

        public int PlaylistIndex => this.pl_index + 1;
        public int PlaylistLength => this.playlist.Count;

        public void Next() {
            if (!CheckIndex(this.pl_index + 1)) {
                this.OnPlaylistFinished(this, new EventArgs());
                return;
            }
            this.Stop();
            this.pl_index ++;
            while (!this.Play()) pl_index ++;
        }

        public void Prev() {
            if (!CheckIndex(this.pl_index - 1)) return;
            this.Stop();
            this.pl_index --;
            while (!this.Play()) pl_index --;
        }

        public double GetPositionInSeconds() {
            long pos = Bass.BASS_ChannelGetPosition(this.handle, 0);
            return Bass.BASS_ChannelBytes2Seconds(this.handle, pos);
        }

        public bool SetPositionInSeconds(double secs) {
            long pos = Bass.BASS_ChannelSeconds2Bytes(this.handle, secs);
            return Bass.BASS_ChannelSetPosition(this.handle, pos, 0) != 0;
        }

        public double GetLengthInSeconds() {
            long len = Bass.BASS_ChannelGetLength(this.handle, 0);
            return Bass.BASS_ChannelBytes2Seconds(this.handle, len);
        }

        public void SetVolume(float vol) {
            if (vol > 1.5f || vol < 0.0f) return;
            vol = (float) Math.Round(vol, 3);
            this.lastVolume = vol;
            Bass.BASS_ChannelSetAttribute(this.handle, 2 /* BASS_ATTRIB_VOL */, vol);
        }

        unsafe public float GetVolume() {
            float vol = 0;
            Bass.BASS_ChannelGetAttribute(this.handle, 2, &vol);
            return vol;
        }
    }

    public class MPServer {

        private TcpListener server;
        private TcpClient   client;
        private MusicPlayer player;

        public MPServer(string host, int port) {
            this.server = new TcpListener(IPAddress.Parse(host), port);
            this.player = new MusicPlayer();
            this.player.AddFiles(Directory.GetFiles("/home/ph03nix/Music/", "*.mp3", SearchOption.AllDirectories));
            this.player.ShufflePlaylist();
        }

        private void Log(string message) {
            Console.WriteLine($"[{DateTime.Now}]:dnmps: {message}");
        }

        public void Listen() {
            this.server.Start();
            Log($"TCP Server running on {this.server.LocalEndpoint}");
            this.player.Play();
            Log("Player started");
            Log("Waiting for clients...");
            while(true) {
                if(this.server.Pending() && this.client == null) {
                    this.client = this.server.AcceptTcpClient();
                    new Thread(() => {
                        HandleClient(this.client);
                    }).Start();
                } else {
                    if(!this.player.IsPlaying() && !this.player.IsPaused() && !this.player.Loading) {
                        player.Next();
                    }
                    Thread.Sleep(500);
                }
            }
        }

        private void HandleClient(TcpClient client) {
            Log($"Client connected. Address: {client.Client.RemoteEndPoint}");
            while(true) {
                try {
                    var data = new byte[1024];
                    client.Client.Receive(data);
                    string cmd = System.Text.Encoding.Default.GetString(data).Replace("\0", "").Trim();
                    Log($"Data received: {cmd} -- Handling...");
                    string response = ExecuteCommand(cmd);
                    // Log($"Response: {response}");
                    SendDataToClient(response);
                } catch {
                    Log("Client disconnected");
                    this.client = null;
                    return;
                }
            }
        }

        public string ExecuteCommand(string cmd) {
            switch(cmd) {
                case "play":
                    this.player.Play();
                    break;
                case "stop":
                    this.player.Stop();
                    break;
                case "pause":
                    this.player.Pause();
                    break;
                case "resume":
                    this.player.Resume();
                    break;
                case "get_player_state":
                    return this.player.IsPlaying() ? "playing" : (this.player.IsPaused() ? "paused" : "unknown");
                case "volume_up":
                    this.player.SetVolume(this.player.GetVolume() + 0.05f);
                    break;
                case "volume_down":
                    this.player.SetVolume(this.player.GetVolume() - 0.05f);
                    break;
                case "forward":
                    this.player.SetPositionInSeconds(this.player.GetPositionInSeconds() + 5);
                    break;
                case "backward":
                    this.player.SetPositionInSeconds(this.player.GetPositionInSeconds() - 5);
                    break;
                case "long_forward":
                    this.player.SetPositionInSeconds(this.player.GetPositionInSeconds() + 30);
                    break;
                case "long_backward":
                    this.player.SetPositionInSeconds(this.player.GetPositionInSeconds() - 30);
                    break;
                case "get_current_song":
                    return this.player.GetCurrentSong();
                case "get_position":
                    return this.player.GetPositionInSeconds().ToString();
                case "get_length":
                    return this.player.GetLengthInSeconds().ToString();
                case "get_pl_index":
                    return this.player.PlaylistIndex.ToString();
                case "get_pl_length":
                    return this.player.PlaylistLength.ToString();
                case "get_volume":
                    return this.player.GetVolume().ToString();
                case "next":
                    this.player.Next();
                    break;
                case "prev":
                    this.player.Prev();
                    break;
                default:
                    break;
            }
            return "null";
        }

        public void SendDataToClient(string data) {
            byte[] encoded = System.Text.Encoding.Default.GetBytes(data);
            this.client.Client.Send(encoded);
        }
    }

    public static class Program {
        public static void Main(string[] args) {
            MPServer server = new MPServer("127.0.0.1", 2806);
            Console.WriteLine("Server listening");
            server.Listen();
        }
    }
}