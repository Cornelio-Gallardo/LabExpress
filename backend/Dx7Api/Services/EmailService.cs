using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dx7Api.Services;

public interface IEmailService
{
    Task<bool> SendAsync(string toEmail, string toName, string subject, string htmlBody);
    Task<bool> SendPasswordResetAsync(string toEmail, string toName, string resetLink);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        var host     = _config["Email:SmtpHost"] ?? "";
        var port     = int.Parse(_config["Email:SmtpPort"] ?? "587");
        var useSsl   = bool.Parse(_config["Email:UseSsl"] ?? "true");
        var from     = _config["Email:FromAddress"] ?? "noreply@dx7.local";
        var fromName = _config["Email:FromName"] ?? "Dx7";
        var username = _config["Email:Username"] ?? "";
        var password = _config["Email:Password"] ?? "";

        // If no SMTP configured, log the email to console (dev mode)
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username))
        {
            _logger.LogWarning("EMAIL (no SMTP configured — console only):");
            _logger.LogWarning("  To:      {Email}", toEmail);
            _logger.LogWarning("  Subject: {Subject}", subject);
            _logger.LogWarning("  Body:    {Body}", htmlBody);
            return true; // Pretend it worked in dev
        }

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl   = useSsl,
                Credentials = new NetworkCredential(username, password)
            };

            var msg = new MailMessage
            {
                From       = new MailAddress(from, fromName),
                Subject    = subject,
                Body       = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(new MailAddress(toEmail, toName));

            await client.SendMailAsync(msg);
            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            return false;
        }
    }

    public async Task<bool> SendPasswordResetAsync(string toEmail, string toName, string resetLink)
    {
        var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='font-family:Inter,sans-serif;background:#f8fafc;margin:0;padding:32px'>
  <div style='max-width:480px;margin:0 auto;background:white;border-radius:12px;border:1px solid #e2e8f0;overflow:hidden'>
    
    <div style='background:#1a56db;padding:24px 32px'>
      <div style='color:white;font-size:20px;font-weight:700'>Dx7 Clinical System</div>
      <div style='color:#bfdbfe;font-size:13px;margin-top:4px'>LABExpress</div>
    </div>

    <div style='padding:32px'>
      <div style='font-size:22px;font-weight:700;color:#0f172a;margin-bottom:8px'>Reset your password</div>
      <div style='color:#64748b;font-size:14px;margin-bottom:24px'>
        Hi {toName}, a password reset was requested for your Dx7 account.
        Click the button below to set a new password.
      </div>

      <a href='{resetLink}' style='display:inline-block;background:#1a56db;color:white;padding:12px 28px;
         border-radius:8px;text-decoration:none;font-weight:600;font-size:15px;margin-bottom:24px'>
        Reset Password
      </a>

      <div style='background:#f8fafc;border-radius:8px;padding:14px;margin-bottom:24px'>
        <div style='font-size:11px;color:#94a3b8;margin-bottom:4px'>Or copy this link:</div>
        <div style='font-size:12px;color:#475569;word-break:break-all;font-family:monospace'>{resetLink}</div>
      </div>

      <div style='font-size:12px;color:#94a3b8;border-top:1px solid #f1f5f9;padding-top:16px'>
        This link expires in <strong>2 hours</strong>. If you didn't request this, ignore this email —
        your password won't change.
      </div>
    </div>
  </div>
</body>
</html>";

        return await SendAsync(toEmail, toName, "Reset your Dx7 password", html);
    }
}