using System;
using System.IO;
using System.Net;
using System.Net.Mail;

namespace SimpleAccountability
{
    public static class EmailSender
    {
        private const string ThreadSubject = "Accountability Mail";

        public static void SendAck(AppSettings settings)
        {
            if (!IsConfigured(settings)) return;
            using var mail = new MailMessage
            {
                From = new MailAddress(settings.SmtpUsername),
                Subject = ThreadSubject,
                Body = $"[Startup] Monitoring begun at {DateTime.Now:G}"
            };
            mail.To.Add(settings.DestinationEmail);
            SendViaSmtp(settings, mail);
        }

        public static void SendScreenshot(AppSettings settings, byte[] imageBytes)
        {
            if (!IsConfigured(settings)) return;
            using var mail = new MailMessage
            {
                From = new MailAddress(settings.SmtpUsername),
                Subject = ThreadSubject,
                Body = $"Screenshot taken at {DateTime.Now:G}"
            };
            mail.To.Add(settings.DestinationEmail);
            mail.Attachments.Add(
                new Attachment(new MemoryStream(imageBytes), "screenshot.jpg"));
            SendViaSmtp(settings, mail);
        }

        public static void SendActivation(AppSettings settings)
        {
            if (!IsConfigured(settings)) return;
            using var mail = new MailMessage
            {
                From = new MailAddress(settings.SmtpUsername),
                Subject = ThreadSubject,
                Body = $"[Activated] Monitoring resumed at {DateTime.Now:G}"
            };
            mail.To.Add(settings.DestinationEmail);
            SendViaSmtp(settings, mail);
        }

        public static void SendDeactivation(AppSettings settings)
        {
            if (!IsConfigured(settings)) return;
            using var mail = new MailMessage
            {
                From = new MailAddress(settings.SmtpUsername),
                Subject = ThreadSubject,
                Body = $"[Deactivated] Monitoring stopped at {DateTime.Now:G}"
            };
            mail.To.Add(settings.DestinationEmail);
            SendViaSmtp(settings, mail);
        }

        public static void SendExit(AppSettings settings)
        {
            if (!IsConfigured(settings)) return;
            using var mail = new MailMessage
            {
                From = new MailAddress(settings.SmtpUsername),
                Subject = ThreadSubject,
                Body = $"[Exit] Application exited at {DateTime.Now:G}"
            };
            mail.To.Add(settings.DestinationEmail);
            SendViaSmtp(settings, mail);
        }

        private static bool IsConfigured(AppSettings s)
        {
            return
                !string.IsNullOrWhiteSpace(s.SmtpUsername) &&
                !string.IsNullOrWhiteSpace(s.SmtpPassword) &&
                !string.IsNullOrWhiteSpace(s.DestinationEmail);
        }

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
    }
}
