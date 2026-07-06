namespace AppStatServer.Auth;

// Posted as JSON to /login by the login page.
public record LoginRequest(string? Username, string? Password);
