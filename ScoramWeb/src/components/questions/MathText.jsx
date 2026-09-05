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
// PYQ reasoning questions routinely use $, %, @, # as LITERAL operator-substitution symbols, not
// math syntax -- e.g. "If @ = x, # = -, $ = /, and % = +, find the value of: 8 @ 3 # 4 % 6 $ 2".
// That's a "define a symbol, then reuse it" pattern, so it very commonly contains a matched PAIR of
// $ characters -- exactly what the regex-based version of this function treated as one giant inline
// math expression, silently losing everything between the two $ signs (and if a literal % fell
// inside that span, KaTeX/LaTeX treats % as a comment marker and drops everything after it too,
// which is what made "है, तो मान ज्ञात कीजिए: 8 @ 3 # 4 % 6" specifically vanish). The stored text was
// always correct -- see it reappear whole in the edit textarea -- only this rendering path mangled
// it, in both the admin preview AND the saved list view, since both go through MathText.
//
// looksLikeMath (below) is the guard against exactly that: genuine LaTeX is Latin letters, digits,
// and backslash commands, so Devanagari inside a $...$ span, an unescaped % (LaTeX's comment
// character -- real intentional math escapes it as \%), or an implausibly long span are all strong
// signals that a pair of $ delimiters was actually two unrelated literal symbols, not one math
// expression. When that's the case, the span renders as plain text, $ signs and all, instead of
// being sent to KaTeX.
//
// A hand-rolled scanner rather than a single regex, because handling \$ (backslash-escaped literal
// dollar sign) and "does this pair actually look like math" both require inspecting a candidate
// span's content before committing to treating it as a delimiter pair -- a single regex can capture
// the span but can't conditionally un-match it and retry from the opening $ if the content fails
// the check.
const DEVANAGARI_RE = /[\u0900-\u097F]/;
const UNESCAPED_PERCENT_RE = /(?<!\\)%/;
const MAX_PLAUSIBLE_MATH_LENGTH = 150;

function looksLikeMath(content) {
  if (!content) return false;
  if (content.length > MAX_PLAUSIBLE_MATH_LENGTH) return false;
  if (DEVANAGARI_RE.test(content)) return false;
  if (UNESCAPED_PERCENT_RE.test(content)) return false;
  return true;
}

function splitMathSegments(text) {
  if (!text) return [];
  const segments = [];
  let textBuffer = "";
  let i = 0;

  function flushText() {
    if (textBuffer) {
      segments.push({ type: "text", content: textBuffer });
      textBuffer = "";
    }
  }

  while (i < text.length) {
    // \$ is always a literal dollar sign, math or not.
    if (text[i] === "\\" && text[i + 1] === "$") {
      textBuffer += "$";
      i += 2;
      continue;
    }

    if (text[i] === "$") {
      const isDisplay = text[i + 1] === "$";
      const delimiter = isDisplay ? "$$" : "$";
      const searchFrom = i + delimiter.length;

      let closeIndex = -1;
      let j = searchFrom;
      while (j < text.length) {
        if (text[j] === "\\" && text[j + 1] === "$") { j += 2; continue; }
        if (!isDisplay && text[j] === "\n") break; // inline math can't span a line break
        if (text.startsWith(delimiter, j)) { closeIndex = j; break; }
        j++;
      }

      if (closeIndex !== -1) {
        const rawContent = text.slice(searchFrom, closeIndex);
        if (looksLikeMath(rawContent)) {
          flushText();
          segments.push({ type: "math", display: isDisplay, content: rawContent.replace(/\\\$/g, "$") });
          i = closeIndex + delimiter.length;
          continue;
        }
        // Doesn't look like math -- fall through and render just this opening $ as a literal
        // character. The $ we found as a candidate close keeps its own turn to pair with a LATER $
        // as we keep scanning forward.
      }
    }

    textBuffer += text[i];
    i++;
  }

  flushText();
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
