export function isAdminRole(role: string | null | undefined): boolean {
  if (!role) return false;
  return role.trim().toLowerCase() === "admin";
}

export function hasAdminRole(roles: string[] | null | undefined): boolean {
  if (!roles || roles.length === 0) return false;
  return roles.some((role) => isAdminRole(role));
}
