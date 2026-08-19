// Registered from src/utils/push.js. Runs in its own worker thread, separate from the React app --
// this is what lets a notification show up even when the Scoram tab isn't the active/focused one
// (it still needs the tab to be open somewhere though; see the caveat in the delivered summary
// about this not being "close the browser entirely" style push).

self.addEventListener("push", (event) => {
  let data = { title: "Scoram", body: "" };
  try {
    if (event.data) data = event.data.json();
  } catch {
    if (event.data) data = { title: "Scoram", body: event.data.text() };
  }

  const options = {
    body: data.body || "",
    icon: "/favicon.png",
    badge: "/favicon.png",
    data: { url: data.url || "/" },
  };

  event.waitUntil(self.registration.showNotification(data.title || "Scoram", options));
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const url = event.notification.data?.url || "/";

  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clientList) => {
      for (const client of clientList) {
        if (client.url.startsWith(self.location.origin) && "focus" in client) {
          client.postMessage({ type: "navigate", url });
          return client.focus();
        }
      }
      if (self.clients.openWindow) return self.clients.openWindow(url);
    })
  );
});
