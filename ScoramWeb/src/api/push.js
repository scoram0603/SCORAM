import { apiFetch } from "./client";

export function getVapidPublicKey() {
  return apiFetch("/api/push/vapid-public-key");
}

export function subscribeToPush({ endpoint, p256dh, auth }) {
  return apiFetch("/api/push/subscribe", {
    method: "POST",
    auth: true,
    body: { endpoint, p256dh, auth },
  });
}

export function unsubscribeFromPush(endpoint) {
  return apiFetch("/api/push/unsubscribe", {
    method: "POST",
    auth: true,
    body: { endpoint },
  });
}
