import { apiFetch } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET /api/admin/tasks/mine  (Admin, Super Admin) -- your own assigned tasks
export function listMyTasks(token, { status } = {}) {
  return apiFetch(`/api/admin/tasks/mine${toQueryString({ status })}`, { token });
}

// GET /api/admin/tasks  (Super Admin only) -- every task, optional filters
export function listAllTasks(token, { status, assignedTo } = {}) {
  return apiFetch(`/api/admin/tasks${toQueryString({ status, assignedTo })}`, { token });
}

// POST /api/admin/tasks  (Super Admin only) -- assign a new task
export function createTask(token, { title, description, assignedToAdminId, deadline }) {
  return apiFetch("/api/admin/tasks", {
    method: "POST",
    token,
    body: { title, description: description || null, assignedToAdminId, deadline: deadline || null },
  });
}

// PATCH /api/admin/tasks/{id}/status -- the assigned admin (their own tasks) or any Super Admin
export function updateTaskStatus(token, id, status) {
  return apiFetch(`/api/admin/tasks/${id}/status`, {
    method: "PATCH",
    token,
    body: { status },
  });
}

// PATCH /api/admin/tasks/{id}  (Super Admin only) -- edit/reassign
export function editTask(token, id, { title, description, assignedToAdminId, deadline }) {
  return apiFetch(`/api/admin/tasks/${id}`, {
    method: "PATCH",
    token,
    body: { title, description, assignedToAdminId, deadline },
  });
}

// DELETE /api/admin/tasks/{id}  (Super Admin only)
export function deleteTask(token, id) {
  return apiFetch(`/api/admin/tasks/${id}`, { method: "DELETE", token });
}
