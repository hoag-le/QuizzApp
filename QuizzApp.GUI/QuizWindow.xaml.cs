using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using QuizzApp.BLL;

namespace QuizzApp.GUI
{
    public partial class QuizWindow : Window
    {
        private readonly Exam _exam;
        private readonly List<Question> _questions;
        private readonly AttemptRecord _attempt;

        private int _currentIndex = 0;
        private bool _isSubmitted = false;
        private bool _practiceRevealCurrent = false;
        private bool _isShowingInternalDialog = false;

        private DispatcherTimer _timer;
        private DateTime _deadlineUtc;
        private int _remainingSeconds;

        // Low-level keyboard hook to block Alt+Tab, Win key, Alt+Esc during Real Exam mode
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_TAB = 0x09;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_F4 = 0x73;
        private const int LLKHF_ALTDOWN = 0x20;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc _keyboardProc;
        private IntPtr _hookId = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        public bool ExamCompleted => _isSubmitted;

        public QuizWindow(Exam exam, List<Question> questions, AttemptRecord resumedAttempt = null)
        {
            InitializeComponent();
            _exam = exam;
            _attempt = resumedAttempt ?? QuizService.StartNewAttempt(exam);

            // Shuffle questions / answers deterministically using Attempt.RandomSeed
            _questions = PrepareQuestions(questions, _exam.ShuffleQuestions, _exam.ShuffleAnswers, _attempt.RandomSeed);

            // Restore saved progress if resuming an attempt
            if (resumedAttempt != null)
            {
                var savedMap = QuizService.GetSavedAnswersForAttempt(_attempt.Id);
                foreach (var q in _questions)
                {
                    if (savedMap.TryGetValue(q.Id, out var savedAns))
                    {
                        q.SelectedOption = savedAns.SelectedOptions;
                        q.IsFlagged = savedAns.IsFlagged;
                    }
                }
            }

            txtExamTitle.Text = $"Đề: {_exam.Title}";
            txtExamModeBadge.Text = _exam.IsPracticeMode ? "Chế độ Luyện tập" : "Chế độ Thi thật";
            badgeExamMode.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_exam.IsPracticeMode ? "#D97706" : "#3563E9"));

            if (_exam.IsPracticeMode)
            {
                btnPracticeCheck.Visibility = Visibility.Visible;
            }
            else
            {
                EnableRealExamLockMode();
            }

            _deadlineUtc = _attempt.StartedAtUtc.AddMinutes(_exam.DurationMinutes);

            BuildQuestionPalette();
            DisplayCurrentQuestion();
            UpdateProgressHeader();
            StartTimer();
        }

        private void EnableRealExamLockMode()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Topmost = true;

            if (_hookId == IntPtr.Zero)
            {
                _keyboardProc = HookCallback;
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(curModule.ModuleName), 0);
                }
            }
        }

        private void DisableRealExamLockMode()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }

            Topmost = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Maximized;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_isSubmitted && !_exam.IsPracticeMode &&
                (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                KBDLLHOOKSTRUCT kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                bool altDown = (kb.flags & LLKHF_ALTDOWN) != 0;
                bool ctrlDown = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

                // Block Alt+Tab, Alt+Esc, Alt+F4, Ctrl+Esc, Left/Right Windows key
                if ((altDown && (kb.vkCode == VK_TAB || kb.vkCode == VK_ESCAPE || kb.vkCode == VK_F4)) ||
                    (ctrlDown && kb.vkCode == VK_ESCAPE) ||
                    kb.vkCode == VK_LWIN || kb.vkCode == VK_RWIN)
                {
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isSubmitted && !_exam.IsPracticeMode)
            {
                if (e.Key == Key.System || e.Key == Key.LWin || e.Key == Key.RWin ||
                    (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                {
                    e.Handled = true;
                }
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (!_isSubmitted && !_exam.IsPracticeMode && !_isShowingInternalDialog)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_isSubmitted && !_exam.IsPracticeMode && !_isShowingInternalDialog)
                    {
                        WindowState = WindowState.Maximized;
                        Topmost = true;
                        Activate();
                        Focus();
                    }
                }), DispatcherPriority.ApplicationIdle);
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (!_isSubmitted && !_exam.IsPracticeMode && WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Maximized;
                Activate();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            DisableRealExamLockMode();
        }

        private List<Question> PrepareQuestions(List<Question> rawQuestions, bool shuffleQuestions, bool shuffleAnswers, int seed)
        {
            var rng = new Random(seed);
            var list = rawQuestions.ToList();

            if (shuffleQuestions)
            {
                list = list.OrderBy(_ => rng.Next()).ToList();
            }

            for (int i = 0; i < list.Count; i++)
            {
                var q = list[i];
                q.Index = i + 1;

                if (shuffleAnswers)
                {
                    var keys = new[] { "A", "B", "C", "D" }.OrderBy(_ => rng.Next()).ToArray();
                    q.DisplayKeyA = keys[0];
                    q.DisplayKeyB = keys[1];
                    q.DisplayKeyC = keys[2];
                    q.DisplayKeyD = keys[3];
                }
                else
                {
                    q.DisplayKeyA = "A";
                    q.DisplayKeyB = "B";
                    q.DisplayKeyC = "C";
                    q.DisplayKeyD = "D";
                }
            }

            return list;
        }

        private string GetOptionTextByOriginalKey(Question q, string origKey)
        {
            switch (origKey)
            {
                case "A": return q.OptionA;
                case "B": return q.OptionB;
                case "C": return q.OptionC;
                case "D": return q.OptionD;
                default: return "";
            }
        }

        private void StartTimer()
        {
            RecalculateRemainingTime();
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) =>
            {
                RecalculateRemainingTime();

                if (_remainingSeconds <= 0)
                {
                    _timer.Stop();
                    _isShowingInternalDialog = true;
                    MessageBox.Show(this, "Đã hết thời gian làm bài! Hệ thống tự động thu bài và chấm điểm.",
                        "Hết giờ làm bài", MessageBoxButton.OK, MessageBoxImage.Information);
                    _isShowingInternalDialog = false;
                    SubmitQuiz(isAutoSubmitted: true);
                }
            };
            _timer.Start();
        }

        private void RecalculateRemainingTime()
        {
            // Specification 6.2: Remaining = StartedAt + Duration - DateTime.UtcNow
            TimeSpan rem = _deadlineUtc - DateTime.UtcNow;
            _remainingSeconds = Math.Max(0, (int)Math.Ceiling(rem.TotalSeconds));

            int mins = _remainingSeconds / 60;
            int secs = _remainingSeconds % 60;
            txtTimer.Text = $"{mins:D2}:{secs:D2}";

            if (_remainingSeconds <= 60 && !_isSubmitted)
            {
                borderTimer.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F1D1D"));
                borderTimer.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            }
        }

        private void BuildQuestionPalette()
        {
            wrapQuestionPalette.Children.Clear();
            for (int i = 0; i < _questions.Count; i++)
            {
                int qIndex = i;
                var btn = new Button
                {
                    Content = (i + 1).ToString(),
                    Width = 40,
                    Height = 38,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand,
                    Tag = qIndex
                };
                btn.Click += (s, e) =>
                {
                    _currentIndex = qIndex;
                    _practiceRevealCurrent = false;
                    DisplayCurrentQuestion();
                };
                wrapQuestionPalette.Children.Add(btn);
            }
            RefreshPaletteStyles();
        }

        private void RefreshPaletteStyles()
        {
            bool filterWrongOnly = _isSubmitted && chkFilterWrongOnly.IsChecked == true;

            for (int i = 0; i < wrapQuestionPalette.Children.Count; i++)
            {
                var btn = (Button)wrapQuestionPalette.Children[i];
                var q = _questions[i];
                bool isCurrent = (i == _currentIndex);

                btn.Visibility = (filterWrongOnly && q.IsCorrect) ? Visibility.Collapsed : Visibility.Visible;
                btn.Content = q.IsFlagged && !_isSubmitted ? $"{i + 1}⚑" : (i + 1).ToString();

                string bgHex = "#F1F5F9";
                string fgHex = "#334155";
                string borderHex = isCurrent ? "#1E293B" : "#CBD5E1";
                double borderThickness = isCurrent ? 2.5 : 1;

                if (_isSubmitted)
                {
                    if (q.IsCorrect)
                    {
                        bgHex = "#10B981";
                        fgHex = "#FFFFFF";
                        borderHex = isCurrent ? "#064E3B" : "#10B981";
                    }
                    else
                    {
                        bgHex = "#EF4444";
                        fgHex = "#FFFFFF";
                        borderHex = isCurrent ? "#7F1D1D" : "#EF4444";
                    }
                }
                else if (q.IsFlagged)
                {
                    bgHex = "#F59E0B";
                    fgHex = "#FFFFFF";
                    borderHex = isCurrent ? "#78350F" : "#F59E0B";
                }
                else if (q.IsAnswered)
                {
                    bgHex = "#3563E9";
                    fgHex = "#FFFFFF";
                    borderHex = isCurrent ? "#172554" : "#3563E9";
                }

                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
                btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex));
                btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderHex));
                btn.BorderThickness = new Thickness(borderThickness);
            }
        }

        private void DisplayCurrentQuestion()
        {
            if (_questions.Count == 0) return;

            var q = _questions[_currentIndex];
            txtQuestionNumber.Text = $"Câu {_currentIndex + 1} / {_questions.Count}";
            txtQuestionContent.Text = q.QuestionText;

            txtDifficultyBadge.Text = $"Độ khó: {q.DifficultyDisplay}";
            string diffBgHex = string.Equals(q.Difficulty, "Hard", StringComparison.OrdinalIgnoreCase) ? "#FEE2E2"
                : (string.Equals(q.Difficulty, "Medium", StringComparison.OrdinalIgnoreCase) ? "#FEF3C7" : "#DCFCE7");
            badgeDifficulty.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(diffBgHex));
            string diffFgHex = string.Equals(q.Difficulty, "Hard", StringComparison.OrdinalIgnoreCase) ? "#B91C1C"
                : (string.Equals(q.Difficulty, "Medium", StringComparison.OrdinalIgnoreCase) ? "#B45309" : "#15803D");
            txtDifficultyBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(diffFgHex));

            txtQuestionTypeBadge.Text = q.IsMultipleChoice ? "☑ Chọn nhiều đáp án đúng" : "◉ Chọn 1 đáp án đúng";

            txtOptA.Text = GetOptionTextByOriginalKey(q, q.DisplayKeyA);
            txtOptB.Text = GetOptionTextByOriginalKey(q, q.DisplayKeyB);
            txtOptC.Text = GetOptionTextByOriginalKey(q, q.DisplayKeyC);
            txtOptD.Text = GetOptionTextByOriginalKey(q, q.DisplayKeyD);

            btnPrev.IsEnabled = _currentIndex > 0;
            btnNext.IsEnabled = _currentIndex < _questions.Count - 1;

            string flagText = q.IsFlagged ? "⚑ Đã đánh dấu chưa chắc chắn" : "⚑ Đánh dấu câu chưa chắc chắn";
            var flagBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(q.IsFlagged ? "#FEF3C7" : "#FFFFFF"));
            var flagFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(q.IsFlagged ? "#B45309" : "#334155"));
            var flagBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString(q.IsFlagged ? "#F59E0B" : "#CBD5E1"));
            btnFlagQuestion.Content = flagText;
            btnFlagQuestion.Background = flagBg;
            btnFlagQuestion.Foreground = flagFg;
            btnFlagQuestion.BorderBrush = flagBorder;
            btnHeaderFlagQuestion.Content = flagText;
            btnHeaderFlagQuestion.Background = flagBg;
            btnHeaderFlagQuestion.Foreground = flagFg;
            btnHeaderFlagQuestion.BorderBrush = flagBorder;
            btnFlagQuestion.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(q.IsFlagged ? "#FEF3C7" : "#FFFFFF"));
            btnFlagQuestion.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(q.IsFlagged ? "#B45309" : "#334155"));

            bool showAnswerOverlay = _isSubmitted || _practiceRevealCurrent;

            StyleOptionBox(q.DisplayKeyA, borderOptA, circleOptA, lblOptA, q, showAnswerOverlay);
            StyleOptionBox(q.DisplayKeyB, borderOptB, circleOptB, lblOptB, q, showAnswerOverlay);
            StyleOptionBox(q.DisplayKeyC, borderOptC, circleOptC, lblOptC, q, showAnswerOverlay);
            StyleOptionBox(q.DisplayKeyD, borderOptD, circleOptD, lblOptD, q, showAnswerOverlay);

            if (showAnswerOverlay)
            {
                badgeReviewStatus.Visibility = Visibility.Visible;
                if (q.IsCorrect)
                {
                    badgeReviewStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7"));
                    txtReviewStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15803D"));
                    txtReviewStatus.Text = "✓ Chính xác";
                }
                else
                {
                    badgeReviewStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2"));
                    txtReviewStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
                    txtReviewStatus.Text = q.IsAnswered ? "✗ Chưa chính xác" : "✗ Bỏ trống";
                }

                if (!string.IsNullOrWhiteSpace(q.Explanation))
                {
                    borderExplanation.Visibility = Visibility.Visible;
                    txtExplanation.Text = q.Explanation;
                }
                else
                {
                    borderExplanation.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                badgeReviewStatus.Visibility = Visibility.Collapsed;
                borderExplanation.Visibility = Visibility.Collapsed;
            }

            RefreshPaletteStyles();
        }

        private void StyleOptionBox(string originalKey, Border cardBorder, Border circle, TextBlock circleLbl, Question q, bool revealAnswer)
        {
            bool isSelected = q.IsOptionSelected(originalKey);
            bool isCorrectOption = q.IsOptionCorrect(originalKey);

            string cardBg = "#F8FAFC";
            string cardBorderHex = "#E2E8F0";
            string circleBg = "#E2E8F0";
            string circleFg = "#334155";

            if (!revealAnswer)
            {
                if (isSelected)
                {
                    cardBg = "#EFF6FF";
                    cardBorderHex = "#3563E9";
                    circleBg = "#3563E9";
                    circleFg = "#FFFFFF";
                }
            }
            else
            {
                if (isCorrectOption)
                {
                    cardBg = "#DCFCE7";
                    cardBorderHex = "#10B981";
                    circleBg = "#10B981";
                    circleFg = "#FFFFFF";
                }
                else if (isSelected && !isCorrectOption)
                {
                    cardBg = "#FEE2E2";
                    cardBorderHex = "#EF4444";
                    circleBg = "#EF4444";
                    circleFg = "#FFFFFF";
                }
            }

            cardBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cardBg));
            cardBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cardBorderHex));
            circle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(circleBg));
            circleLbl.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(circleFg));
        }

        private void SelectOptionByDisplaySlot(string originalKey)
        {
            if (_isSubmitted) return;
            var q = _questions[_currentIndex];
            q.ToggleSelectedOption(originalKey);

            // Auto-save answer immediately to SQLite AttemptAnswers
            QuizService.SaveAttemptAnswerProgress(_attempt.Id, q.Id, q.SelectedOption, q.IsFlagged);

            UpdateProgressHeader();
            DisplayCurrentQuestion();
        }

        private void UpdateProgressHeader()
        {
            int answered = _questions.Count(q => q.IsAnswered);
            int flagged = _questions.Count(q => q.IsFlagged);
            txtProgressSubtitle.Text = _isSubmitted
                ? $"Chế độ xem lại đáp án  •  Đã làm: {answered}/{_questions.Count} câu"
                : $"Đã làm: {answered}/{_questions.Count} câu  •  Chưa chắc chắn: {flagged}  •  Đã tự động lưu";
        }

        private void OptionA_Click(object sender, MouseButtonEventArgs e) => SelectOptionByDisplaySlot(_questions[_currentIndex].DisplayKeyA);
        private void OptionB_Click(object sender, MouseButtonEventArgs e) => SelectOptionByDisplaySlot(_questions[_currentIndex].DisplayKeyB);
        private void OptionC_Click(object sender, MouseButtonEventArgs e) => SelectOptionByDisplaySlot(_questions[_currentIndex].DisplayKeyC);
        private void OptionD_Click(object sender, MouseButtonEventArgs e) => SelectOptionByDisplaySlot(_questions[_currentIndex].DisplayKeyD);

        private void BtnFlagQuestion_Click(object sender, RoutedEventArgs e)
        {
            if (_isSubmitted) return;
            var q = _questions[_currentIndex];
            q.IsFlagged = !q.IsFlagged;
            QuizService.SaveAttemptAnswerProgress(_attempt.Id, q.Id, q.SelectedOption, q.IsFlagged);
            UpdateProgressHeader();
            DisplayCurrentQuestion();
        }

        private void BtnPracticeCheck_Click(object sender, RoutedEventArgs e)
        {
            _practiceRevealCurrent = !_practiceRevealCurrent;
            DisplayCurrentQuestion();
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
                _practiceRevealCurrent = false;
                DisplayCurrentQuestion();
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < _questions.Count - 1)
            {
                _currentIndex++;
                _practiceRevealCurrent = false;
                DisplayCurrentQuestion();
            }
        }

        private void ChkFilterWrongOnly_Changed(object sender, RoutedEventArgs e)
        {
            RefreshPaletteStyles();
            if (chkFilterWrongOnly.IsChecked == true && _questions[_currentIndex].IsCorrect)
            {
                int firstWrong = _questions.FindIndex(q => !q.IsCorrect);
                if (firstWrong >= 0)
                {
                    _currentIndex = firstWrong;
                    DisplayCurrentQuestion();
                }
            }
        }

        private void BtnSubmitOrClose_Click(object sender, RoutedEventArgs e)
        {
            if (_isSubmitted)
            {
                Close();
                return;
            }

            int unanswered = _questions.Count(q => !q.IsAnswered);
            int flagged = _questions.Count(q => q.IsFlagged);

            string confirmMsg = unanswered > 0
                ? $"Bạn còn {unanswered} câu hỏi chưa làm" + (flagged > 0 ? $" và {flagged} câu đang gắn cờ đánh dấu." : ".") + "\nBạn có chắc chắn muốn nộp bài ngay không?"
                : "Bạn đã trả lời tất cả các câu hỏi.\nXác nhận nộp bài?";

            _isShowingInternalDialog = true;
            var result = MessageBox.Show(this, confirmMsg, "Xác nhận nộp bài", MessageBoxButton.YesNo, MessageBoxImage.Question);
            _isShowingInternalDialog = false;
            if (result == MessageBoxResult.Yes)
            {
                SubmitQuiz(isAutoSubmitted: false);
            }
        }

        private void SubmitQuiz(bool isAutoSubmitted)
        {
            if (_isSubmitted) return;
            _isSubmitted = true;
            _timer?.Stop();
            DisableRealExamLockMode();

            int total = _questions.Count;
            int correctCount = _questions.Count(q => q.IsCorrect);
            int blankCount = _questions.Count(q => !q.IsAnswered);
            int wrongCount = total - correctCount - blankCount;
            double score = total > 0 ? (double)correctCount / total * 10.0 : 0.0;

            int elapsedSeconds = Math.Max(1, (int)(DateTime.UtcNow - _attempt.StartedAtUtc).TotalSeconds);
            QuizService.CompleteAttemptAndSaveHistory(_attempt.Id, _exam.Id, _exam.Title, score, correctCount, total, elapsedSeconds, isAutoSubmitted);

            borderResultSummary.Visibility = Visibility.Visible;
            chkFilterWrongOnly.Visibility = Visibility.Visible;
            btnFlagQuestion.Visibility = Visibility.Collapsed;
            btnHeaderFlagQuestion.Visibility = Visibility.Collapsed;
            btnPracticeCheck.Visibility = Visibility.Collapsed;

            txtFinalScore.Text = $"{score:0.0} / 10";
            txtFinalDetail.Text = $"Đúng: {correctCount}  |  Sai: {wrongCount}  |  Bỏ trống: {blankCount}";

            btnSubmitOrClose.Content = "Thoát xem lại";
            btnSubmitOrClose.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));

            UpdateProgressHeader();
            DisplayCurrentQuestion();

            _isShowingInternalDialog = true;
            MessageBox.Show(
                this,
                $"Đã hoàn thành bài kiểm tra!\n\n" +
                $"• Đề thi: {_exam.Title}\n" +
                $"• Điểm số: {score:0.0} / 10\n" +
                $"• Số câu đúng: {correctCount}/{total} câu\n" +
                $"• Số câu sai: {wrongCount} câu | Bỏ trống: {blankCount} câu\n\n" +
                $"Bạn có thể xem lại từng câu hỏi, lời giải thích hoặc lọc 'Chỉ hiển thị câu trả lời sai'.",
                "Kết quả bài thi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _isShowingInternalDialog = false;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isSubmitted)
            {
                if (!_exam.IsPracticeMode)
                {
                    e.Cancel = true;
                    _isShowingInternalDialog = true;
                    MessageBox.Show(
                        this,
                        "Trong chế độ Thi thật, bạn không được phép thoát hoặc chuyển cửa sổ khi chưa nộp bài!\nVui lòng nhấn nút 'Nộp bài' ở góc dưới bên phải để hoàn tất bài thi.",
                        "Cảnh báo chế độ Thi thật",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    _isShowingInternalDialog = false;
                    return;
                }

                _isShowingInternalDialog = true;
                var res = MessageBox.Show(
                    this,
                    "Bài thi chưa được nộp!\n\n" +
                    "• Chọn [Yes] để Tạm dừng và lưu tiến độ làm bài.\n" +
                    "• Chọn [No] để Hủy bỏ lượt thi này.\n" +
                    "• Chọn [Cancel] để ở lại tiếp tục làm bài.",
                    "Tạm dừng bài thi",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                _isShowingInternalDialog = false;

                if (res == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (res == MessageBoxResult.No)
                {
                    QuizService.AbandonInProgressAttempt(_attempt.Id);
                }
                _timer?.Stop();
                DisableRealExamLockMode();
            }
        }
    }
}
