using System;
using System.IO;
using System.Net;
using System.Net.Mail;

namespace SimpleAccountability
{
    public static class EmailSender
    {
        private const string ThreadSubject = "Accountability Mail";
        private static readonly string _logFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimpleAccountability",
            "email_errors.log");

        public static void SendAck(AppSettings settings)
            => SafeSend(MakeMail(settings, $"[Startup] Monitoring begun at {DateTime.Now:G}"));

        public static void SendActivation(AppSettings settings)
            => SafeSend(MakeMail(settings, $"[Activated] Monitoring resumed at {DateTime.Now:G}"));

        public static void SendDeactivation(AppSettings settings)
            => SafeSend(MakeMail(settings, $"[Deactivated] Monitoring stopped at {DateTime.Now:G}"));

        public static void SendExit(AppSettings settings)
            => SafeSend(MakeMail(settings, $"[Exit] Application exited at {DateTime.Now:G}"));

        public static void SendScreenshot(AppSettings settings, byte[] imageBytes)
        {
            var mail = MakeMail(settings, $"Screenshot taken at {DateTime.Now:G}");
            mail.Attachments.Add(new Attachment(
                new MemoryStream(imageBytes), "screenshot.jpg"));
            // Let exceptions bubble so our timer logic can queue on failure
            SendViaSmtp(settings, mail);
        }

        private static MailMessage MakeMail(AppSettings settings, string body)
        {
            var m = new MailMessage
            {
                From = new MailAddress(settings.SmtpUsername),
                Subject = ThreadSubject,
                Body = body
            };
            m.To.Add(settings.DestinationEmail);

            // Thread‑ID headers
            m.Headers.Add("In-Reply-To", settings.ThreadId);
            m.Headers.Add("References", settings.ThreadId);

            return m;
        }

        /// <summary>
        /// Sends a control message, swallowing SMTP exceptions so the app doesn't crash.
        /// </summary>
        private static void SafeSend(MailMessage mail)
        {
            try
            {
                // Use default credentials? No—always assume SMTP via settings
                var settings = AppSettings.Load(  // reload to get latest
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "SimpleAccountability",
                        "settings.json"));
                SendViaSmtp(settings, mail);
            }
            catch (SmtpException ex)
            {
                LogError("SMTP", ex);
            }
            catch (Exception ex)
            {
                LogError("General", ex);
            }
        }

        /// <summary>
        /// Actually performs the SMTP send; throws on failure.
        /// </summary>
        private static void SendViaSmtp(AppSettings settings, MailMessage mail)
        {
            using var client = new SmtpClient("smtp.gmail.com", 587)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(
                    settings.SmtpUsername,
                    settings.SmtpPassword)
            };
            client.Send(mail);
        }

        private static void LogError(string category, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                File.AppendAllText(_logFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {category} ERROR: {ex}\r\n");
            }
            catch
            {
                // If logging fails, give up silently
            }
        }
    }
}
