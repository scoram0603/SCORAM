import { useEffect, useRef, useState } from "react";
import { Mic, Square, Send, X, Loader2 } from "lucide-react";

// Picks whatever MIME type the browser's MediaRecorder actually supports -- Chrome/Firefox default
// to webm/opus, Safari needs mp4. Falls back to letting the browser choose if none of these match.
function pickMimeType() {
  const candidates = ["audio/webm", "audio/mp4", "audio/ogg"];
  for (const type of candidates) {
    if (window.MediaRecorder?.isTypeSupported?.(type)) return type;
  }
  return undefined;
}

function extensionFor(mimeType) {
  if (mimeType?.includes("mp4")) return "m4a";
  if (mimeType?.includes("ogg")) return "ogg";
  return "webm";
}

function formatTime(totalSeconds) {
  const m = Math.floor(totalSeconds / 60);
  const s = totalSeconds % 60;
  return `${m}:${String(s).padStart(2, "0")}`;
}

// Three states: idle (mic button) -> recording (timer + stop) -> recorded (preview + send/discard).
export default function AudioRecorder({ onSend, onClose }) {
  const [phase, setPhase] = useState("idle"); // idle | recording | recorded | error
  const [seconds, setSeconds] = useState(0);
  const [recordedBlob, setRecordedBlob] = useState(null);
  const [previewUrl, setPreviewUrl] = useState(null);
  const [sending, setSending] = useState(false);
  const [errorMessage, setErrorMessage] = useState("");

  const mediaRecorderRef = useRef(null);
  const chunksRef = useRef([]);
  const streamRef = useRef(null);
  const timerRef = useRef(null);

  useEffect(() => {
    startRecording();
    return () => {
      clearInterval(timerRef.current);
      streamRef.current?.getTracks().forEach((t) => t.stop());
      if (previewUrl) URL.revokeObjectURL(previewUrl);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function startRecording() {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;

      const mimeType = pickMimeType();
      const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
      mediaRecorderRef.current = recorder;
      chunksRef.current = [];

      recorder.ondataavailable = (e) => {
        if (e.data.size > 0) chunksRef.current.push(e.data);
      };
      recorder.onstop = () => {
        const blob = new Blob(chunksRef.current, { type: mimeType || "audio/webm" });
        setRecordedBlob(blob);
        setPreviewUrl(URL.createObjectURL(blob));
        setPhase("recorded");
        stream.getTracks().forEach((t) => t.stop());
      };

      recorder.start();
      setPhase("recording");
      setSeconds(0);
      timerRef.current = setInterval(() => setSeconds((s) => s + 1), 1000);
    } catch {
      setPhase("error");
      setErrorMessage("Couldn't access your microphone. Check your browser's permission settings.");
    }
  }

  function stopRecording() {
    clearInterval(timerRef.current);
    mediaRecorderRef.current?.stop();
  }

  function discard() {
    clearInterval(timerRef.current);
    mediaRecorderRef.current?.stream?.getTracks().forEach((t) => t.stop());
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    onClose();
  }

  async function handleSend() {
    if (!recordedBlob) return;
    setSending(true);
    const file = new File([recordedBlob], `voice-note.${extensionFor(recordedBlob.type)}`, { type: recordedBlob.type });
    await onSend(file, seconds);
    setSending(false);
  }

  if (phase === "error") {
    return (
      <div className="flex items-center gap-2 rounded-xl2 border border-red-100 bg-red-50 px-3 py-2.5 text-xs font-medium text-red-600">
        <span className="flex-1">{errorMessage}</span>
        <button type="button" onClick={onClose} aria-label="Close">
          <X className="h-3.5 w-3.5" strokeWidth={2.25} />
        </button>
      </div>
    );
  }

  if (phase === "recording") {
    return (
      <div className="flex items-center gap-3 rounded-xl2 border border-primary-100 bg-white px-3.5 py-2.5">
        <span className="relative flex h-2.5 w-2.5 shrink-0">
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-red-400 opacity-75" />
          <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-red-500" />
        </span>
        <span className="flex-1 text-sm font-semibold tabular-nums text-ink-900">{formatTime(seconds)}</span>
        <button type="button" onClick={discard} aria-label="Cancel recording" className="text-ink-400 hover:text-ink-600">
          <X className="h-4 w-4" strokeWidth={2.25} />
        </button>
        <button
          type="button"
          onClick={stopRecording}
          aria-label="Stop recording"
          className="flex h-9 w-9 items-center justify-center rounded-full bg-red-500 text-white hover:bg-red-600"
        >
          <Square className="h-3.5 w-3.5" strokeWidth={2.5} fill="currentColor" />
        </button>
      </div>
    );
  }

  if (phase === "recorded") {
    return (
      <div className="flex items-center gap-2.5 rounded-xl2 border border-primary-100 bg-white px-3 py-2.5">
        <Mic className="h-4 w-4 shrink-0 text-primary-600" strokeWidth={2.25} />
        <audio src={previewUrl} controls className="h-9 min-w-0 flex-1" />
        <span className="shrink-0 text-xs font-medium tabular-nums text-ink-400">{formatTime(seconds)}</span>
        <button type="button" onClick={discard} aria-label="Discard recording" className="text-ink-400 hover:text-ink-600">
          <X className="h-4 w-4" strokeWidth={2.25} />
        </button>
        <button
          type="button"
          onClick={handleSend}
          disabled={sending}
          aria-label="Send voice note"
          className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary-600 text-white hover:bg-primary-700 disabled:opacity-60"
        >
          {sending ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} /> : <Send className="h-4 w-4" strokeWidth={2.25} />}
        </button>
      </div>
    );
  }

  return null; // "idle" is instantaneous -- startRecording() fires as soon as this mounts
}
