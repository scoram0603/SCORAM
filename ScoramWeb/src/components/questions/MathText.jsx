import { useMemo } from "react";
import katex from "katex";
import { API_BASE_URL } from "../../api/client";

function imgSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

// Splits a plain-text question/option/explanation string into alternating text and math segments,
// on $$...$$ (display math) and $...$ (inline math) -- the delimiters the spec's LaTeX examples use
// throughout (e.g. "$x:y = 3:5$", "$$\frac{x^2-5x+6}{x-2}=0$$"). A string with no $ at all (the
// overwhelming majority of existing questions, before this feature existed) comes back as a single
// plain-text segment, so nothing changes visually for them.
//
// Deliberately a small hand-rolled splitter rather than a markdown/regex-heavy parser: the only
// syntax this needs to recognize is "math between $ signs", and escaping is limited to allowing a
// backslash-escaped literal \$ if a question genuinely needs a dollar sign in its text.
function splitMathSegments(text) {
  if (!text) return [];
  const segments = [];
  // Matches $$...$$ first (non-greedy, no unescaped $ inside), falling back to $...$. The
  // alternation order matters -- checking $ before $$ would split "$$x$$" into two adjacent
  // inline-math segments ("" and "x") instead of one display-math segment.
  const pattern = /\$\$([^$]+?)\$\$|\$([^$\n]+?)\$/g;
  let lastIndex = 0;
  let match;
  while ((match = pattern.exec(text)) !== null) {
    if (match.index > lastIndex) {
      segments.push({ type: "text", content: text.slice(lastIndex, match.index) });
    }
    if (match[1] !== undefined) {
      segments.push({ type: "math", display: true, content: match[1] });
    } else {
      segments.push({ type: "math", display: false, content: match[2] });
    }
    lastIndex = pattern.lastIndex;
  }
  if (lastIndex < text.length) {
    segments.push({ type: "text", content: text.slice(lastIndex) });
  }
  return segments;
}

// Renders one math segment via KaTeX. throwOnError:false means a malformed LaTeX expression (a
// genuine typo an admin made, not a bug in this component) renders as KaTeX's own inline red error
// text rather than crashing the page -- which doubles as a visible "this doesn't parse" signal
// during admin preview (spec section 39: "the admin should immediately see what the student will
// see"), including when that's an error.
function renderKatexHtml(tex, displayMode) {
  try {
    return katex.renderToString(tex, {
      throwOnError: false,
      displayMode,
      strict: false,
      // Never trust admin-authored LaTeX with commands that can embed arbitrary URLs/resources
      // (\href, \includegraphics, ...) -- this content can come from a bulk-uploaded ZIP, not just
      // hand-typed admin input.
      trust: false,
    });
  } catch {
    // Extremely defensive fallback -- throwOnError:false already covers ordinary parse errors, but
    // this catches anything else KaTeX might throw so one bad expression never blanks the page.
    return null;
  }
}

// Renders a plain string with inline/display LaTeX math mixed in -- the default rendering path for
// every existing QuestionText/OptionX/Explanation field, math or not. Wrap any place one of those
// fields is displayed (admin or student) with this instead of rendering the raw string directly.
export function MathText({ text, className }) {
  const segments = useMemo(() => splitMathSegments(text || ""), [text]);

  if (segments.length === 0) return null;

  return (
    <span className={className}>
      {segments.map((seg, i) => {
        if (seg.type === "text") {
          // whitespace-pre-wrap preserves the line breaks a question/explanation may contain,
          // matching how a plain <p> with a raw string rendered before this component existed.
          return (
            <span key={i} className="whitespace-pre-wrap">
              {seg.content}
            </span>
          );
        }
        const html = renderKatexHtml(seg.content, seg.display);
        if (html == null) {
          // KaTeX itself failed unexpectedly (see renderKatexHtml) -- fall back to showing the raw
          // LaTeX source rather than silently dropping it, so the admin still sees *something* is
          // there and can fix it.
          return <span key={i} className="font-mono text-red-500">${seg.content}$</span>;
        }
        return (
          <span
            key={i}
            className={seg.display ? "my-1 block overflow-x-auto" : "inline-block"}
            // KaTeX's own output, generated with trust:false above -- not raw user input.
            dangerouslySetInnerHTML={{ __html: html }}
          />
        );
      })}
    </span>
  );
}

// Renders an optional ContentBlocks sequence (see backend DTOs/ContentBlockDto.cs: an ordered list
// of { type: "text"|"math"|"image"|"table", content }) if one is present, OR falls back to plain
// MathText(fallbackText) if not -- so a caller can use this everywhere a question's main body
// renders without needing to know in advance whether that particular question has rich content or
// is a plain pre-existing one. This is the one place ContentBlocks actually gets displayed; nothing
// else in the frontend needs to know the field exists.
export function RichQuestionBody({ contentBlocks, fallbackText, className }) {
  if (!Array.isArray(contentBlocks) || contentBlocks.length === 0) {
    return <MathText text={fallbackText} className={className} />;
  }

  return (
    <div className={className}>
      {contentBlocks.map((block, i) => {
        if (block.type === "image") {
          return imgSrc(block.content) ? (
            <img key={i} src={imgSrc(block.content)} alt="" className="my-2 max-h-64 rounded-lg border border-primary-100" />
          ) : null;
        }
        if (block.type === "table") {
          // Table content is a JSON-encoded 2D array of strings (see ContentBlockDto's own
          // comment) -- rendered as a plain HTML table. Falls back to nothing (not raw JSON) if a
          // block's content doesn't actually parse, rather than dumping unreadable JSON in front of
          // a student.
          let rows;
          try {
            rows = JSON.parse(block.content);
          } catch {
            return null;
          }
          if (!Array.isArray(rows)) return null;
          return (
            <table key={i} className="my-2 border-collapse text-sm">
              <tbody>
                {rows.map((row, r) => (
                  <tr key={r}>
                    {(Array.isArray(row) ? row : [row]).map((cell, c) => (
                      <td key={c} className="border border-primary-100 px-2 py-1">
                        <MathText text={String(cell ?? "")} />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          );
        }
        // "text" and "math" blocks render the same way -- MathText already handles inline math
        // inside a "text" block, and a "math" block is just content that's entirely math (wrapping
        // it in $$...$$ if it isn't already, so a block author doesn't have to remember to).
        const content = block.type === "math" && !/^\s*\${1,2}/.test(block.content || "")
          ? `$$${block.content}$$`
          : block.content;
        return <MathText key={i} text={content} className="block" />;
      })}
    </div>
  );
}
