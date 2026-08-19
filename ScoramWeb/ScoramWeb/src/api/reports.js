import { apiFetch } from "./client";

// POST /api/questions/{questionId}/reports -- report a legacy PYQ question
export function reportQuestion(questionId, { reportType, description, proofUrl } = {}) {
  return apiFetch(`/api/questions/${questionId}/reports`, {
    method: "POST",
    auth: true,
    body: { reportType, description, proofUrl },
  });
}

// POST /api/question-bank/{questionId}/reports -- report a Question Bank question
export function reportQuestionBankQuestion(questionId, { reportType, description, proofUrl } = {}) {
  return apiFetch(`/api/question-bank/${questionId}/reports`, {
    method: "POST",
    auth: true,
    body: { reportType, description, proofUrl },
  });
}

// The reasons offered in the "Report Question" modal -- must match ScoramAPI.Enums.ReportType.
export const REPORT_REASONS = [
  { value: "WrongAnswer", label: "Wrong Answer" },
  { value: "WrongOption", label: "Wrong Option" },
  { value: "WrongQuestionStatement", label: "Wrong Question" },
  { value: "IncorrectExplanation", label: "Wrong Explanation" },
  { value: "TypingMistake", label: "Typographical Error" },
  { value: "Duplicate", label: "Duplicate Question" },
  { value: "IncorrectExamYear", label: "Incorrect Exam/Year" },
  { value: "Other", label: "Other" },
];
