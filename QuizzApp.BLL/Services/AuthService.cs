using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using QuizzApp.DAL;

namespace QuizzApp.BLL
{
    /// <summary>
    /// Quản lý xác thực người dùng (Đăng nhập, Đăng ký) và quản trị danh sách tài khoản.
    /// </summary>
    public static class AuthService
    {
        public static UserAccount AuthenticateUser(string username, string password)
        {
            string cleanUser = (username ?? "").Trim().ToLower();
            if (string.IsNullOrEmpty(cleanUser)) return null;

            using (var db = new QuizzDbContext())
            {
                var u = db.Users.FirstOrDefault(x => x.Username.ToLower() == cleanUser);
                if (u != null && PasswordHasher.Verify(password ?? "", u.PasswordHash))
                {
                    if (!PasswordHasher.IsDynamicSaltedHash(u.PasswordHash))
                    {
                        u.PasswordHash = PasswordHasher.Hash(password ?? "");
                        db.SaveChanges();
                    }

                    return new UserAccount
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Password = u.PasswordHash,
                        FullName = u.FullName,
                        Role = u.Role
                    };
                }
            }
            return null;
        }

        public static bool RegisterUser(string username, string password, string fullName, out string errorMessage)
        {
            errorMessage = "";
            username = (username ?? "").Trim();
            fullName = (fullName ?? "").Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(fullName))
            {
                errorMessage = "Vui lòng nhập đầy đủ tên đăng nhập, mật khẩu và họ tên.";
                return false;
            }

            using (var db = new QuizzDbContext())
            {
                string lower = username.ToLower();
                if (db.Users.Any(u => u.Username.ToLower() == lower))
                {
                    errorMessage = "Tên đăng nhập này đã tồn tại. Vui lòng chọn tên khác!";
                    return false;
                }

                db.Users.Add(new QuizzApp.DAL.Entities.User
                {
                    Username = username,
                    PasswordHash = PasswordHasher.Hash(password),
                    FullName = fullName,
                    Role = "Student"
                });
                db.SaveChanges();
            }
            return true;
        }

        public static List<UserAccount> GetAllUsers()
        {
            using (var db = new QuizzDbContext())
            {
                var users = db.Users.OrderBy(u => u.Id).ToList();
                return users.Select(u => new UserAccount
                {
                    Id = u.Id,
                    Username = u.Username,
                    Password = u.PasswordHash,
                    FullName = u.FullName,
                    Role = u.Role,
                    CreatedAt = ""
                }).ToList();
            }
        }

        public static bool AddUserByAdmin(string username, string password, string fullName, string role, out string errorMessage)
        {
            errorMessage = "";
            username = (username ?? "").Trim();
            fullName = (fullName ?? "").Trim();
            string entityRole = MapRoleToEntityRole(role);

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(fullName))
            {
                errorMessage = "Vui lòng nhập đầy đủ Tên đăng nhập, Mật khẩu và Họ tên!";
                return false;
            }

            using (var db = new QuizzDbContext())
            {
                string lower = username.ToLower();
                if (db.Users.Any(u => u.Username.ToLower() == lower))
                {
                    errorMessage = "Tên đăng nhập này đã tồn tại trong hệ thống!";
                    return false;
                }

                db.Users.Add(new QuizzApp.DAL.Entities.User
                {
                    Username = username,
                    PasswordHash = PasswordHasher.Hash(password),
                    FullName = fullName,
                    Role = entityRole
                });
                db.SaveChanges();
            }
            return true;
        }

        public static bool UpdateUserByAdmin(int userId, string username, string newPasswordOptional, string fullName, string role, out string errorMessage)
        {
            errorMessage = "";
            username = (username ?? "").Trim();
            fullName = (fullName ?? "").Trim();
            string entityRole = MapRoleToEntityRole(role);

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(fullName))
            {
                errorMessage = "Tên đăng nhập và Họ tên không được để trống!";
                return false;
            }

            using (var db = new QuizzDbContext())
            {
                string lower = username.ToLower();
                if (db.Users.Any(u => u.Username.ToLower() == lower && u.Id != userId))
                {
                    errorMessage = "Tên đăng nhập này đã được sử dụng bởi tài khoản khác!";
                    return false;
                }

                var user = db.Users.FirstOrDefault(u => u.Id == userId);
                if (user == null)
                {
                    errorMessage = "Không tìm thấy tài khoản cần cập nhật!";
                    return false;
                }

                user.Username = username;
                user.FullName = fullName;
                user.Role = entityRole;
                if (!string.IsNullOrWhiteSpace(newPasswordOptional))
                {
                    user.PasswordHash = PasswordHasher.Hash(newPasswordOptional.Trim());
                }
                db.SaveChanges();
            }
            return true;
        }

        public static void DeleteUser(int userId)
        {
            using (var db = new QuizzDbContext())
            {
                var attempts = db.ExamAttempts.Include(a => a.AttemptDetails).Where(a => a.UserId == userId).ToList();
                foreach (var att in attempts)
                {
                    db.AttemptDetails.RemoveRange(att.AttemptDetails);
                }
                db.ExamAttempts.RemoveRange(attempts);

                var user = db.Users.FirstOrDefault(u => u.Id == userId);
                if (user != null)
                {
                    db.Users.Remove(user);
                }
                db.SaveChanges();
            }
        }

        private static string MapRoleToEntityRole(string uiRole)
        {
            if (string.Equals(uiRole, "Admin", StringComparison.OrdinalIgnoreCase)) return "Admin";
            if (string.Equals(uiRole, "Teacher", StringComparison.OrdinalIgnoreCase)) return "Teacher";
            return "Student";
        }
    }
}
