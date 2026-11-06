using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using QuizzApp.BLL;

namespace QuizzApp.GUI
{
    public partial class LoginWindow : Window
    {
        private bool _isRegisterMode = false;
        private int _failedAttempts = 0;
        private DateTime _lockedUntilUtc = DateTime.MinValue;
        private static readonly string RememberFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remember_user.txt");

        public LoginWindow()
        {
            InitializeComponent();
            UpdateServerStatusBadge();
            LoadRememberedUser();

            try
            {
                QuizService.InitializeDatabase();
            }
            catch (Exception ex)
            {
                OpenServerConfigPanel();
                ShowServerConfigMessage(
                    string.Format("Không kết nối được tới MySQL ({0}). Vui lòng kiểm tra IP máy chủ LAN hoặc cấu hình kết nối bên dưới.\nChi tiết: {1}",
                        QuizService.GetServerSummaryText(), ex.Message),
                    isError: true);
            }
        }

        private void UpdateServerStatusBadge()
        {
            QuizService.GetServerConfig(out string host, out string port, out string dbName, out string user, out string pass);
            txtServerStatus.Text = string.Format("{0}:{1} ({2})", host, port, dbName);
            txtCfgHost.Text = host;
            txtCfgPort.Text = port;
            txtCfgDbName.Text = dbName;
            txtCfgUser.Text = user;
            txtCfgPass.Password = pass;
        }

        private void LoadRememberedUser()
        {
            if (File.Exists(RememberFile))
            {
                string savedUser = File.ReadAllText(RememberFile).Trim();
                if (!string.IsNullOrEmpty(savedUser))
                {
                    txtUsername.Text = savedUser;
                    txtPassword.Password = "123456";
                    chkRememberMe.IsChecked = true;
                    return;
                }
            }
            txtUsername.Text = "admin";
            txtPassword.Password = "123456";
        }

        private void BtnOpenServerConfig_Click(object sender, RoutedEventArgs e)
        {
            OpenServerConfigPanel();
        }

        private void OpenServerConfigPanel()
        {
            UpdateServerStatusBadge();
            txtServerConfigMsg.Visibility = Visibility.Collapsed;
            panelAuthForm.Visibility = Visibility.Collapsed;
            panelServerConfig.Visibility = Visibility.Visible;
        }

        private void BtnCloseServerConfig_Click(object sender, RoutedEventArgs e)
        {
            panelServerConfig.Visibility = Visibility.Collapsed;
            panelAuthForm.Visibility = Visibility.Visible;
        }

        private void BtnSaveServerConfig_Click(object sender, RoutedEventArgs e)
        {
            string host = txtCfgHost.Text.Trim();
            string port = txtCfgPort.Text.Trim();
            string dbName = txtCfgDbName.Text.Trim();
            string user = txtCfgUser.Text.Trim();
            string pass = txtCfgPass.Password;

            if (QuizService.ConfigureServerConnection(host, port, dbName, user, pass, out string errorMsg))
            {
                UpdateServerStatusBadge();
                ShowServerConfigMessage("Kết nối máy chủ MySQL thành công! Đã lưu cấu hình và đồng bộ dữ liệu.", isError: false);
                MessageBox.Show(
                    string.Format("Đã kết nối thành công tới máy chủ MySQL:\n{0}:{1} (CSDL: {2})", host, port, dbName),
                    "Kết nối LAN thành công",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                panelServerConfig.Visibility = Visibility.Collapsed;
                panelAuthForm.Visibility = Visibility.Visible;
            }
            else
            {
                ShowServerConfigMessage("Không thể kết nối: " + errorMsg, isError: true);
            }
        }

        private void ShowServerConfigMessage(string msg, bool isError)
        {
            txtServerConfigMsg.Text = msg;
            txtServerConfigMsg.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isError ? "#DC2626" : "#15803D"));
            txtServerConfigMsg.Visibility = Visibility.Visible;
        }

        private void BtnFillAdmin_Click(object sender, RoutedEventArgs e)
        {
            panelServerConfig.Visibility = Visibility.Collapsed;
            panelAuthForm.Visibility = Visibility.Visible;
            if (_isRegisterMode) ToggleMode();
            txtUsername.Text = "admin";
            txtPassword.Password = "123456";
            txtError.Visibility = Visibility.Collapsed;
        }

        private void BtnFillStudent_Click(object sender, RoutedEventArgs e)
        {
            panelServerConfig.Visibility = Visibility.Collapsed;
            panelAuthForm.Visibility = Visibility.Visible;
            if (_isRegisterMode) ToggleMode();
            txtUsername.Text = "hocvien";
            txtPassword.Password = "123456";
            txtError.Visibility = Visibility.Collapsed;
        }

        private void BtnToggleMode_Click(object sender, RoutedEventArgs e)
        {
            ToggleMode();
        }

        private void ToggleMode()
        {
            _isRegisterMode = !_isRegisterMode;
            txtError.Visibility = Visibility.Collapsed;

            if (_isRegisterMode)
            {
                txtFormTitle.Text = "Đăng ký tài khoản mới";
                txtFormSubtitle.Text = "Tạo tài khoản học sinh để tham gia làm bài thi";
                panelFullName.Visibility = Visibility.Visible;
                chkRememberMe.Visibility = Visibility.Collapsed;
                btnSubmit.Content = "Hoàn tất đăng ký";
                txtSwitchPrompt.Text = "Đã có tài khoản?";
                btnToggleMode.Content = "Quay lại đăng nhập";
                txtUsername.Text = "";
                txtPassword.Password = "";
                txtFullName.Text = "";
            }
            else
            {
                txtFormTitle.Text = "Đăng nhập tài khoản";
                txtFormSubtitle.Text = "Nhập thông tin tài khoản để truy cập hệ thống";
                panelFullName.Visibility = Visibility.Collapsed;
                chkRememberMe.Visibility = Visibility.Visible;
                btnSubmit.Content = "Đăng nhập →";
                txtSwitchPrompt.Text = "Chưa có tài khoản học sinh?";
                btnToggleMode.Content = "Đăng ký ngay";
            }
        }

        private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnSubmit_Click(sender, e);
            }
        }

        private void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            txtError.Visibility = Visibility.Collapsed;

            if (DateTime.UtcNow < _lockedUntilUtc)
            {
                int waitSecs = (int)Math.Ceiling((_lockedUntilUtc - DateTime.UtcNow).TotalSeconds);
                ShowError(string.Format("Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau {0} giây.", waitSecs));
                return;
            }

            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;

            try
            {
                if (_isRegisterMode)
                {
                    string fullName = txtFullName.Text.Trim();
                    if (!QuizService.RegisterUser(username, password, fullName, out string errorMsg))
                    {
                        ShowError(errorMsg);
                        return;
                    }

                    MessageBox.Show("Đăng ký tài khoản học sinh thành công! Hệ thống sẽ tự động đăng nhập.",
                        "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                var user = QuizService.AuthenticateUser(username, password);
                if (user == null)
                {
                    _failedAttempts++;
                    if (_failedAttempts >= 5)
                    {
                        _lockedUntilUtc = DateTime.UtcNow.AddSeconds(30);
                        _failedAttempts = 0;
                        ShowError("Bạn đã nhập sai 5 lần liên tiếp! Tạm khóa đăng nhập trong 30 giây.");
                    }
                    else
                    {
                        ShowError(string.Format("Tên đăng nhập hoặc mật khẩu không chính xác! (Lần thử {0}/5)", _failedAttempts));
                    }
                    return;
                }

                _failedAttempts = 0;
                try
                {
                    if (chkRememberMe.IsChecked == true)
                        File.WriteAllText(RememberFile, username);
                    else if (File.Exists(RememberFile))
                        File.Delete(RememberFile);
                }
                catch { }

                SessionManager.CurrentUser = user;

                if (user.IsAdmin)
                {
                    var adminWin = new AdminWindow();
                    adminWin.Show();
                }
                else
                {
                    var mainWin = new MainWindow();
                    mainWin.Show();
                }

                Close();
            }
            catch (Exception ex)
            {
                ShowError("Lỗi kết nối máy chủ CSDL: " + ex.Message);
            }
        }

        private void ShowError(string msg)
        {
            txtError.Text = msg;
            txtError.Visibility = Visibility.Visible;
        }
    }
}
