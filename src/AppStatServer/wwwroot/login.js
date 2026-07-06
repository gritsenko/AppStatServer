const form = document.getElementById("login-form");
const errorEl = document.getElementById("error");

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  errorEl.textContent = "";

  const username = document.getElementById("username").value;
  const password = document.getElementById("password").value;

  try {
    const res = await fetch("/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username, password }),
    });

    if (res.ok) {
      window.location.href = "/";
    } else if (res.status === 401) {
      errorEl.textContent = "Invalid username or password.";
    } else {
      errorEl.textContent = "Sign in failed (" + res.status + ").";
    }
  } catch {
    errorEl.textContent = "Could not reach the server.";
  }
});
