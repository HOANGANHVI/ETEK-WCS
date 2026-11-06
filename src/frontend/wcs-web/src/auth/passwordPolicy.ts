/** Mirrors the backend password policy (the server decides; this only saves a round trip). */
export function checkPassword(pw: string, username: string): string | null {
  if (pw.length < 10) return 'Mật khẩu phải có ít nhất 10 ký tự.'
  if (!/[A-Za-zÀ-ỹ]/.test(pw) || !/\d/.test(pw)) return 'Mật khẩu phải gồm cả chữ và số.'
  if (username && pw.toLowerCase().includes(username.toLowerCase())) return 'Mật khẩu không được chứa tên đăng nhập.'
  return null
}
