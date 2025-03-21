﻿using FinalAPIDoAn.Data;
using FinalAPIDoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly KetNoiCSDL _dbc;

    public AuthController(AuthService authService, KetNoiCSDL dbc)
    {
        _authService = authService;
        _dbc = dbc;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        // Kiểm tra dữ liệu đầu vào
        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest("Username and password are required.");
        }

        // Kiểm tra xem người dùng đã tồn tại chưa
        var existingUser = await _dbc.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (existingUser != null)
        {
            return Conflict("Username already exists.");
        }

        // Mã hóa mật khẩu
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

        // Tạo người dùng mới
        var newUser = new User
        {
            Username = request.Username,
            PasswordHash = hashedPassword,
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            Role = "Customer", // Mặc định là "Customer"
            CreatedAt = DateTime.UtcNow
        };

        // Lưu người dùng vào cơ sở dữ liệu
        _dbc.Users.Add(newUser);
        await _dbc.SaveChangesAsync();

        return Ok(new { Message = "User registered successfully!" });
    }



    [HttpPost("login")]
    public async Task<IActionResult> Login([FromQuery] string username, [FromQuery] string password)
    {
        var token = await _authService.Login(username, password);
        if (token == null)
        {
            return Unauthorized();
        }
        return Ok(new { Token = token });
    }
    public class RegisterRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
    }
}