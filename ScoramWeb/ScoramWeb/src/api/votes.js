import { apiFetch } from "./client";

function basePath(questionType) {
  return questionType === "bank" ? "/api/question-bank" : "/api/questions";
}

// POST .../vote { isLike } -- toggles: same reaction again retracts it, the other one switches it.
// Returns { likeCount, dislikeCount, myVote }.
export function voteOnQuestion(questionId, isLike, questionType = "paper") {
  return apiFetch(`${basePath(questionType)}/${questionId}/vote`, {
    method: "POST",
    auth: true,
    body: { isLike },
  });
}
