import { RichQuestionBody } from "../questions/MathText";

// Shared rendering for Shared Stimulus (passage / table / chart / image common to several questions) and
// Advanced Paper Instructions. Used by the student TestRunner AND the admin Paper Preview, so what an admin
// previews is exactly what a student sees. Content is the same { type, content } block list as questions, rendered
// by the existing RichQuestionBody (text / math / image / table) -- no second renderer.

// Stimulus images are usually screenshots of tables/charts, so unlike question images they are NOT height-capped;
// they scale to the panel width and never overflow it.
const STIMULUS_IMAGE_CLASS = "my-2 h-auto w-full max-w-full rounded-lg border border-primary-100";

export function StimulusPanel({ stimuli, className = "" }) {
  if (!Array.isArray(stimuli) || stimuli.length === 0) return null;
  return (
    <section
      aria-label="Passage or data for this question"
      className={`min-w-0 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card sm:p-5 ${className}`}
    >
      {stimuli.map((s, i) => (
        <div key={s.id} className={i > 0 ? "mt-4 border-t border-primary-100 pt-4" : ""}>
          {s.title && <p className="mb-2 text-xs font-bold uppercase tracking-wide text-secondary-500">{s.title}</p>}
          {/* overflow-x-auto: a wide table scrolls inside the panel instead of breaking the phone layout */}
          <div className="overflow-x-auto text-[15px] leading-relaxed text-ink-900">
            <RichQuestionBody contentBlocks={s.contentBlocks} fallbackText="" imageClassName={STIMULUS_IMAGE_CLASS} className="space-y-2" />
          </div>
        </div>
      ))}
    </section>
  );
}

export function PaperInstructionsList({ instructions }) {
  if (!Array.isArray(instructions) || instructions.length === 0) return null;
  return (
    <div className="space-y-4">
      {instructions.map((ins) => (
        <div key={ins.id} className="min-w-0">
          {ins.title && <p className="mb-1 text-sm font-bold text-ink-900">{ins.title}</p>}
          <div className="overflow-x-auto text-sm leading-relaxed text-ink-600">
            <RichQuestionBody contentBlocks={ins.contentBlocks} fallbackText="" imageClassName={STIMULUS_IMAGE_CLASS} className="space-y-2" />
          </div>
        </div>
      ))}
    </div>
  );
}
