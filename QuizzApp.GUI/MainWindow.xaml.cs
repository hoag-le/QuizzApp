using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

using QuizzApp.BLL;

namespace QuizzApp.GUI
{
    public partial class MainWindow : Window
    {
        private List<Exam> _allExams = new List<Exam>();
        private bool _onlySavedMode = false;
        private AttemptRecord _pendingAttempt = null;

        public MainWindow()
        {
            InitializeComponent();
            InitializeAppData();
        }

        private void InitializeAppData()
        {
            try
            {
                QuizService.InitializeDatabase();
                if (SessionManager.CurrentUser != null)
                {
                    txtSidebarUser.Text = SessionManager.CurrentUser.FullName;
                    txtSidebarRole.Text = $"{SessionManager.CurrentUser.RoleDisplay} ({SessionManager.CurrentUser.Username})";
                }

                LoadSubjectFilters();
                LoadExams();
                UpdateHeaderStatsBadge();
                CheckForActiveAttempt();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi tạo cơ sở dữ liệu: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSubjectFilters()
        {
            var subjects = QuizService.GetAllSubjects();
            var filterItems = new List<Subject>
            {
                new Subject { Id = 0, Name = "Tất cả môn học" }
            };
            filterItems.AddRange(subjects);
            cboSubjectFilter.ItemsSource = filterItems;
            cboSubjectFilter.SelectedIndex = 0;
        }

        private void CheckForActiveAttempt()
        {
            int uid = SessionManager.CurrentUser?.Id ?? 0;
            _pendingAttempt = QuizService.GetActiveAttemptForUser(uid);
            if (_pendingAttempt != null)
            {
                bannerResumeAttempt.Visibility = Visibility.Visible;
                txtResumeAttemptInfo.Text = $"Đề thi: {_pendingAttempt.ExamTitle} (Bắt đầu lúc {_pendingAttempt.StartedAtUtc.ToLocalTime():HH:mm dd/MM/yyyy})";
            }
            else
            {
                bannerResumeAttempt.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnResumeAttempt_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingAttempt == null) return;
            var exam = _allExams.FirstOrDefault(x => x.Id == _pendingAttempt.ExamId);
            if (exam == null)
            {
                QuizService.AbandonInProgressAttempt(_pendingAttempt.Id);
                CheckForActiveAttempt();
                return;
            }

            var questions = QuizService.GetQuestionsByExamId(exam.Id);
            var quizWin = new QuizWindow(exam, questions, _pendingAttempt)
            {
                Owner = this
            };
            quizWin.ShowDialog();

            CheckForActiveAttempt();
            UpdateHeaderStatsBadge();
            if (quizWin.ExamCompleted)
            {
                BtnNavHistory_Click(null, null);
            }
        }

        private void BtnDiscardAttempt_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingAttempt != null)
            {
                QuizService.AbandonInProgressAttempt(_pendingAttempt.Id);
                CheckForActiveAttempt();
            }
        }

        private void LoadExams()
        {
            _allExams = QuizService.GetAllExams();
            ApplyExamFilter();
        }

        private void ApplyExamFilter()
        {
            if (itemsExams == null) return;

            string keyword = txtSearch?.Text?.Trim().ToLower() ?? "";
            int selectedSubjectId = (cboSubjectFilter?.SelectedItem as Subject)?.Id ?? 0;

            IEnumerable<Exam> filtered = _allExams;

            if (_onlySavedMode)
            {
                filtered = filtered.Where(x => x.IsSaved);
            }

            if (selectedSubjectId > 0)
            {
                filtered = filtered.Where(x => x.SubjectId == selectedSubjectId);
            }

            if (!string.IsNullOrEmpty(keyword))
            {
                filtered = filtered.Where(x =>
                    x.Title.ToLower().Contains(keyword) ||
                    x.Description.ToLower().Contains(keyword) ||
                    x.SubjectDisplay.ToLower().Contains(keyword));
            }

            var resultList = filtered.ToList();
            itemsExams.ItemsSource = resultList;
            txtEmptyExams.Visibility = resultList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateHeaderStatsBadge()
        {
            int? uid = SessionManager.CurrentUser?.Id;
            var history = QuizService.GetExamHistory(uid);
            string userPrefix = SessionManager.CurrentUser != null ? $"{SessionManager.CurrentUser.FullName} • " : "";
            txtQuickStatsBadge.Text = $"{userPrefix}Đã làm: {history.Count} bài";
        }

        private void SetActiveNavButton(Button activeBtn)
        {
            Button[] navButtons = { btnNavExams, btnNavHistory, btnNavSaved, btnNavSettings };
            foreach (var btn in navButtons)
            {
                if (btn == activeBtn)
                {
                    btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3563E9"));
                    btn.Foreground = Brushes.White;
                }
                else
                {
                    btn.Background = Brushes.Transparent;
                    btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"));
                }
            }
        }

        private void BtnNavExams_Click(object sender, RoutedEventArgs e)
        {
            _onlySavedMode = false;
            SetActiveNavButton(btnNavExams);

            txtHeaderTitle.Text = "Thư viện đề thi";
            txtExamSectionHeading.Text = "Danh sách đề thi";
            bannerWelcome.Visibility = Visibility.Visible;

            panelExamsView.Visibility = Visibility.Visible;
            panelHistoryView.Visibility = Visibility.Collapsed;
            panelProfileView.Visibility = Visibility.Collapsed;

            CheckForActiveAttempt();
            LoadExams();
        }

        private void BtnNavSaved_Click(object sender, RoutedEventArgs e)
        {
            _onlySavedMode = true;
            SetActiveNavButton(btnNavSaved);

            txtHeaderTitle.Text = "Đề thi đã lưu";
            txtExamSectionHeading.Text = "Đề đã lưu của bạn";
            bannerWelcome.Visibility = Visibility.Collapsed;

            panelExamsView.Visibility = Visibility.Visible;
            panelHistoryView.Visibility = Visibility.Collapsed;
            panelProfileView.Visibility = Visibility.Collapsed;

            LoadExams();
        }

        private void BtnNavHistory_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNavButton(btnNavHistory);

            txtHeaderTitle.Text = "Lịch sử làm bài";

            panelExamsView.Visibility = Visibility.Collapsed;
            panelHistoryView.Visibility = Visibility.Visible;
            panelProfileView.Visibility = Visibility.Collapsed;

            LoadHistoryView();
        }

        private void LoadHistoryView()
        {
            int? uid = SessionManager.CurrentUser?.Id;
            var history = QuizService.GetExamHistory(uid);
            itemsHistory.ItemsSource = history;
            borderEmptyHistory.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            btnClearHistory.Visibility = history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            txtStatTotalTests.Text = history.Count.ToString();
            if (history.Count > 0)
            {
                txtStatAvgScore.Text = $"{history.Average(h => h.Score):0.0}";
                txtStatBestScore.Text = $"{history.Max(h => h.Score):0.0}";
            }
            else
            {
                txtStatAvgScore.Text = "0.0";
                txtStatBestScore.Text = "0.0";
            }
        }

        private void BtnNavSettings_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNavButton(btnNavSettings);
            txtHeaderTitle.Text = "Cài đặt & Sao lưu hệ thống";

            panelExamsView.Visibility = Visibility.Collapsed;
            panelHistoryView.Visibility = Visibility.Collapsed;
            panelProfileView.Visibility = Visibility.Visible;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyExamFilter();
        }

        private void CboSubjectFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyExamFilter();
        }

        private void BtnStartExam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Exam exam)
            {
                var questions = QuizService.GetQuestionsByExamId(exam.Id);
                if (questions.Count == 0)
                {
                    MessageBox.Show("Đề thi này hiện chưa có câu hỏi nào!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var quizWin = new QuizWindow(exam, questions)
                {
                    Owner = this
                };
                quizWin.ShowDialog();

                CheckForActiveAttempt();
                UpdateHeaderStatsBadge();
                if (quizWin.ExamCompleted)
                {
                    BtnNavHistory_Click(null, null);
                }
            }
        }

        private void BtnToggleSaveExam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Exam exam)
            {
                exam.IsSaved = !exam.IsSaved;
                QuizService.ToggleSaveExam(exam.Id, exam.IsSaved);
                if (_onlySavedMode)
                {
                    ApplyExamFilter();
                }
            }
        }

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ lịch sử làm bài của mình không?",
                "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                int? uid = SessionManager.CurrentUser != null ? (int?)SessionManager.CurrentUser.Id : null;
                QuizService.ClearExamHistory(uid);
                LoadHistoryView();
                UpdateHeaderStatsBadge();
            }
        }

        private void BtnBackupDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Chọn nơi lưu file sao lưu CSDL",
                Filter = "SQLite Database (*.db)|*.db",
                FileName = $"QuizMaster_Backup_{DateTime.Now:yyyyMMdd_HHmm}.db"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    QuizService.BackupDatabase(dlg.FileName);
                    MessageBox.Show($"Đã sao lưu cơ sở dữ liệu thành công tại:\n{dlg.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi sao lưu: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnRestoreDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Chọn file sao lưu CSDL để khôi phục",
                Filter = "SQLite Database (*.db)|*.db"
            };
            if (dlg.ShowDialog() == true)
            {
                var confirm = MessageBox.Show(
                    "Khôi phục CSDL sẽ ghi đè dữ liệu hiện tại bằng dữ liệu trong bản sao lưu.\nBạn có chắc chắn muốn tiếp tục?",
                    "Xác nhận khôi phục", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    try
                    {
                        QuizService.RestoreDatabase(dlg.FileName);
                        LoadSubjectFilters();
                        LoadExams();
                        UpdateHeaderStatsBadge();
                        MessageBox.Show("Đã khôi phục cơ sở dữ liệu thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Lỗi khi khôi phục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnBackToAdmin_Click(object sender, RoutedEventArgs e)
        {
            var adminWin = new AdminWindow();
            adminWin.Show();
            Close();
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            SessionManager.CurrentUser = null;
            var loginWin = new LoginWindow();
            loginWin.Show();
            Close();
        }
    }
}
