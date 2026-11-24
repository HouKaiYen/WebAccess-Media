using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebAccess.Media.Pages;

public sealed class EngineeringModel(EngineeringSettings settings) : PageModel
{
    [BindProperty]
    public ConnectionForm Input { get; set; } = new();
    public string? Feedback { get; private set; }
    public bool IsSuccessful { get; private set; }

    public void OnGet()
    {
        try
        {
            if (settings.Current is ConnectionSettings current)
            {
                Input = new()
                {
                    Ip = Uri.TryCreate(current.ApiBase, UriKind.Absolute, out Uri? address) &&
                        IPAddress.TryParse(address.Host, out IPAddress? ip) ? ip.ToString() : "",
                    Project = current.Project,
                    Username = current.Username
                };
            }
        }
        catch (Exception error) when (IsStorageFailure(error))
        {
            Feedback = "無法讀取本機設定。請以原 Windows 帳號啟動，或重新輸入並儲存連線資料。";
        }
    }

    public Task<IActionResult> OnPostTestAsync() => SubmitAsync(save: false);
    public Task<IActionResult> OnPostSaveAsync() => SubmitAsync(save: true);

    private async Task<IActionResult> SubmitAsync(bool save)
    {
        string password = Input.Password ?? "";
        Input.Password = null;
        ModelState.Remove("Input.Password");
        string? apiBase = WebAccessAddress.FromIp(Input.Ip ?? "");
        if (!ModelState.IsValid || apiBase is null)
        {
            Feedback = "請輸入有效的 IP 位址、專案名稱及帳號。";
            return Page();
        }

        try
        {
            string project = Input.Project.Trim();
            string username = Input.Username.Trim();
            if (password.Length == 0 && settings.Current is ConnectionSettings current &&
                Uri.TryCreate(current.ApiBase, UriKind.Absolute, out Uri? previousAddress) &&
                Uri.TryCreate(apiBase, UriKind.Absolute, out Uri? newAddress) &&
                previousAddress == newAddress && current.Project == project && current.Username == username)
            {
                password = current.Password;
            }
            ConnectionSettings connection = new(apiBase, project, username, password);
            if (save)
            {
                await settings.SaveAsync(connection, HttpContext.RequestAborted);
                Feedback = "已儲存設定，監控會自動套用。";
            }
            else
            {
                await settings.TestAsync(connection, HttpContext.RequestAborted);
                Feedback = "連線測試成功。請按「儲存並開始監控」套用設定。";
            }
            IsSuccessful = true;
        }
        catch (HttpRequestException error) when (error.StatusCode is not null)
        {
            Feedback = $"WebAccess 回傳 HTTP {(int)error.StatusCode.Value}。請確認 API 網址及帳號權限。";
        }
        catch (Exception error) when (IsStorageFailure(error))
        {
            Feedback = "無法讀寫本機設定。請確認 Windows 帳號及資料目錄權限。";
        }
        catch (Exception error) when (!HttpContext.RequestAborted.IsCancellationRequested && WebAccessClient.IsConnectionFailure(error))
        {
            Feedback = "無法取得完整告警清單。請確認網路、API 網址及專案設定。";
        }
        return Page();
    }

    private static bool IsStorageFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or CryptographicException;

    public sealed class ConnectionForm
    {
        [Required, StringLength(64)] public string Ip { get; set; } = "";
        [Required, StringLength(256)] public string Project { get; set; } = "";
        [Required, StringLength(256)] public string Username { get; set; } = "";
        public string? Password { get; set; }
    }
}
