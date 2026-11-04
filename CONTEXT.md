# PROJECT CONTEXT: HỆ THỐNG THI TRẮC NGHIỆM (QUIZZ-APP WPF)
- **Framework:** .NET Framework 4.7.2 (WPF Application), ngôn ngữ C# 7.3.
- **Database & ORM:** MySQL + Entity Framework 6 (`System.Data.Entity`). Đã có sẵn `QuizzDbContext` và 8 bảng trong `QuizzApp.DAL`.
- **Kiến trúc 3 tầng:** `QuizzApp.DAL` (Entities, DbContext) -> `QuizzApp.BLL` (Services xử lý nghiệp vụ) -> `QuizzApp.GUI` (WPF Windows/UserControls).
- **Quy tắc C# 7.3 & EF6 bắt buộc:**
  1. Luôn `using System.Data.Entity;` và `using System.Linq;` khi truy vấn. Khi lấy bảng con phải dùng `.Include(q => q.QuestionOptions)`.
  2. Không dùng cú pháp C# 8+ (không dùng `using var`, không dùng `string?`, không dùng file-scoped namespace).
  3. Mỗi hàm trong tầng BLL luôn khởi tạo `using (var db = new QuizzDbContext()) { ... }` riêng biệt.
  4. Không tự bịa thuộc tính XAML không có trong WPF .NET 4.7.2 (như `PlaceholderText` hay `CornerRadius` trực tiếp trên `Button`/`TextBox`).