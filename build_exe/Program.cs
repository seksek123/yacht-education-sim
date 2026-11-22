using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("서울수상레포츠센터 요트교육 시뮬레이터 v12.2")]
[assembly: AssemblyDescription("Keel boat & ILCA 7 딩기요트 통합 공기역학 시뮬레이터 (Ver 12.2 - Spatial Glass & Luxury Marine Chronometer)")]
[assembly: AssemblyCompany("서울수상레포츠센터 (제작자: 박주호 Park Jooho)")]
[assembly: AssemblyProduct("Yacht Sailing Aerodynamics Simulator")]
[assembly: AssemblyCopyright("Copyright © 2024-2026 박주호 (Park Jooho). All Rights Reserved.")]
[assembly: AssemblyVersion("12.2.0.0")]
[assembly: AssemblyFileVersion("12.2.0.0")]

namespace YachtAeroHUD
{
    static class Program
    {
        private static HttpListener _listener;
        private static DateTime _lastActivity = DateTime.UtcNow;
        private static volatile bool _running = true;

        [STAThread]
        static void Main()
        {
            int port = GetFreePort();
            string prefix = "http://127.0.0.1:" + port + "/";

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(prefix);
                _listener.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "내장 로컬 서버를 구동하는 중 오류가 발생했습니다.\n\n" + ex.Message,
                    "실행 오류 - 요트교육 시뮬레이터",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // 1) 백그라운드 인메모리 HTTP 서버 스레드 시작
            Thread serverThread = new Thread(ServerLoop);
            serverThread.IsBackground = true;
            serverThread.Start();

            // 2) 전용 앱 모드(App Mode) 브라우저 창 기동
            Process browserProc = LaunchBrowserApp(prefix);

            // 3) 브라우저 종료 및 하트비트 감시 루프
            // 시작 직후 20초간은 브라우저 렌더링 준비 시간(Grace Period)을 부여합니다.
            DateTime gracePeriodEnd = DateTime.UtcNow.AddSeconds(20);

            while (_running)
            {
                Thread.Sleep(1000);

                // 브라우저 프로세스가 새 인스턴스로 분리 실행되어 종료된 경우
                if (browserProc != null && browserProc.HasExited)
                {
                    // 일부 브라우저는 이미 실행 중인 프로세스로 핸들을 넘겨주고 즉시 종료될 수 있으므로,
                    // 하트비트가 활성화된 적이 있다면 하트비트 체크로 넘깁니다.
                }

                // 20초 경과 후, 8초 동안 웹 클라이언트로부터 신호가 없으면 앱이 닫힌 것으로 판단하여 종료
                if (DateTime.UtcNow > gracePeriodEnd)
                {
                    if ((DateTime.UtcNow - _lastActivity).TotalSeconds > 8)
                    {
                        break;
                    }
                }
            }

            // 클린업 및 프로세스 완전 종료
            _running = false;
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch { }
        }

        private static int GetFreePort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static void ServerLoop()
        {
            while (_running && _listener != null && _listener.IsListening)
            {
                try
                {
                    HttpListenerContext ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(HandleRequest, ctx);
                }
                catch
                {
                    break;
                }
            }
        }

        private static void HandleRequest(object state)
        {
            HttpListenerContext ctx = (HttpListenerContext)state;
            _lastActivity = DateTime.UtcNow;

            try
            {
                string path = ctx.Request.Url.AbsolutePath;

                // 하트비트 엔드포인트
                if (path == "/api/heartbeat")
                {
                    byte[] ok = Encoding.UTF8.GetBytes("ok");
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/plain";
                    ctx.Response.ContentLength64 = ok.Length;
                    ctx.Response.OutputStream.Write(ok, 0, ok.Length);
                    ctx.Response.Close();
                    return;
                }

                // 브라우저 창 닫힘(beforeunload) 시그널
                if (path == "/api/exit")
                {
                    byte[] bye = Encoding.UTF8.GetBytes("bye");
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/plain";
                    ctx.Response.ContentLength64 = bye.Length;
                    ctx.Response.OutputStream.Write(bye, 0, bye.Length);
                    ctx.Response.Close();

                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Thread.Sleep(300);
                        _running = false;
                        try { _listener.Stop(); } catch { }
                    });
                    return;
                }

                // 정적 리소스 인메모리 매핑
                string resourceName = null;
                string contentType = "application/octet-stream";

                if (path == "/" || path == "/index.html")
                {
                    resourceName = "index.html";
                    contentType = "text/html; charset=utf-8";
                }
                else if (path == "/three.min.js")
                {
                    resourceName = "three.min.js";
                    contentType = "application/javascript; charset=utf-8";
                }
                else if (path == "/app_icon.ico" || path == "/favicon.ico")
                {
                    resourceName = "app_icon.ico";
                    contentType = "image/x-icon";
                }
                else if (path == "/water_bg.mp4")
                {
                    ServeVideo(ctx);
                    return;
                }
                else if (path == "/water_bg.gif")
                {
                    resourceName = "water_bg.gif";
                    contentType = "image/gif";
                }
                else if (path == "/water_bg.jpg" || path == "/water_bg.jpeg")
                {
                    resourceName = "water_bg.jpg";
                    contentType = "image/jpeg";
                }

                if (resourceName != null)
                {
                    string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, resourceName);
                    if (File.Exists(localFile))
                    {
                        using (FileStream fs = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            ctx.Response.StatusCode = 200;
                            ctx.Response.ContentType = contentType;
                            ctx.Response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate");
                            ctx.Response.ContentLength64 = fs.Length;
                            byte[] buf = new byte[64 * 1024];
                            int r;
                            while ((r = fs.Read(buf, 0, buf.Length)) > 0)
                            {
                                ctx.Response.OutputStream.Write(buf, 0, r);
                            }
                            ctx.Response.Close();
                            return;
                        }
                    }

                    Assembly asm = Assembly.GetExecutingAssembly();
                    using (Stream stream = asm.GetManifestResourceStream(resourceName) ?? asm.GetManifestResourceStream("YachtAeroHUD." + resourceName))
                    {
                        if (stream != null)
                        {
                            ctx.Response.StatusCode = 200;
                            ctx.Response.ContentType = contentType;
                            ctx.Response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate");
                            ctx.Response.Headers.Add("X-Content-Type-Options", "nosniff");
                            ctx.Response.ContentLength64 = stream.Length;

                            byte[] buf = new byte[64 * 1024];
                            int read;
                            while ((read = stream.Read(buf, 0, buf.Length)) > 0)
                            {
                                ctx.Response.OutputStream.Write(buf, 0, read);
                            }
                            ctx.Response.Close();
                            return;
                        }
                    }
                }

                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { }
            }
        }

        private static void ServeVideo(HttpListenerContext ctx)
        {
            try
            {
                Stream videoStream = null;
                long totalLength = 0;

                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "water_bg.mp4");
                if (File.Exists(localPath))
                {
                    videoStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    totalLength = videoStream.Length;
                }
                else
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    videoStream = asm.GetManifestResourceStream("water_bg.mp4") ?? asm.GetManifestResourceStream("YachtAeroHUD.water_bg.mp4");
                    if (videoStream != null)
                    {
                        totalLength = videoStream.Length;
                    }
                }

                if (videoStream == null)
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                using (videoStream)
                {
                    long start = 0;
                    long end = totalLength - 1;
                    bool isRange = false;

                    string rangeHeader = ctx.Request.Headers["Range"];
                    if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
                    {
                        string[] parts = rangeHeader.Substring(6).Split('-');
                        long parsedStart;
                        if (long.TryParse(parts[0], out parsedStart))
                        {
                            start = Math.Max(0, Math.Min(parsedStart, totalLength - 1));
                        }
                        long parsedEnd;
                        if (parts.Length > 1 && long.TryParse(parts[1], out parsedEnd))
                        {
                            end = Math.Max(start, Math.Min(parsedEnd, totalLength - 1));
                        }
                        isRange = true;
                    }

                    long sendLength = end - start + 1;

                    ctx.Response.StatusCode = isRange ? 206 : 200;
                    ctx.Response.ContentType = "video/mp4";
                    ctx.Response.Headers.Add("Accept-Ranges", "bytes");
                    ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                    if (isRange)
                    {
                        ctx.Response.Headers.Add("Content-Range", string.Format("bytes {0}-{1}/{2}", start, end, totalLength));
                    }
                    ctx.Response.ContentLength64 = sendLength;

                    // HEAD 요청 시 헤더만 송신하고 본문 쓰기 생략 (ProtocolViolationException 방지)
                    if (ctx.Request.HttpMethod == "HEAD")
                    {
                        ctx.Response.Close();
                        return;
                    }

                    if (start > 0)
                    {
                        videoStream.Seek(start, SeekOrigin.Begin);
                    }

                    try
                    {
                        byte[] buffer = new byte[64 * 1024];
                        long remaining = sendLength;
                        while (remaining > 0)
                        {
                            int toRead = (int)Math.Min(buffer.Length, remaining);
                            int read = videoStream.Read(buffer, 0, toRead);
                            if (read <= 0) break;
                            ctx.Response.OutputStream.Write(buffer, 0, read);
                            remaining -= read;
                        }
                        ctx.Response.Close();
                    }
                    catch (HttpListenerException)
                    {
                        // 클라이언트가 스트림 탐색 중 연결을 닫았을 때 발생하는 정상 예외
                    }
                }
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { }
            }
        }

        private static Process LaunchBrowserApp(string url)
        {
            string[] candidates = new string[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
            };

            foreach (string browserPath in candidates)
            {
                if (File.Exists(browserPath))
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo();
                        psi.FileName = browserPath;
                        // 앱 모드로 주소창, 북마크, 탭 없이 독립 윈도우 창으로 실행
                        psi.Arguments = string.Format("--app=\"{0}\" --window-size=1600,980", url);
                        psi.UseShellExecute = false;
                        return Process.Start(psi);
                    }
                    catch { }
                }
            }

            // Edge/Chrome 미설치 환경 대비 기본 브라우저 대체 실행
            try
            {
                return Process.Start(url);
            }
            catch
            {
                return null;
            }
        }
    }
}
