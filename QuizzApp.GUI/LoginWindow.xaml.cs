using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

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
            try
            {
                QuizService.InitializeDatabase();
                LoadRememberedUser();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi tạo cơ sở dữ liệu: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void BtnFillAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (_isRegisterMode) ToggleMode();
            txtUsername.Text = "admin";
            txtPassword.Password = "123456";
            txtError.Visibility = Visibility.Collapsed;
        }

        private void BtnFillStudent_Click(object sender, RoutedEventArgs e)
        {
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
                ShowError($"Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau {waitSecs} giây.");
                return;
            }

            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;

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
                    ShowError($"Tên đăng nhập hoặc mật khẩu không chính xác! (Lần thử {_failedAttempts}/5)");
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

        private void ShowError(string msg)
        {
            txtError.Text = msg;
            txtError.Visibility = Visibility.Visible;
        }
    }
}
