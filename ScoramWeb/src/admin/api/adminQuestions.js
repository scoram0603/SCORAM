import { apiFetch, apiFetchForm } from "../../api/client";

// Builds the multipart form for create/update -- shared since both endpoints take the same shape
// (text fields + up to 6 optional image files).
function buildQuestionFormData(fields, images) {
  const formData = new FormData();
  Object.entries(fields).forEach(([key, value]) => {
    if (value !== undefined && value !== null) formData.append(key, value);
  });
  Object.entries(images || {}).forEach(([key, file]) => {
    if (file) formData.append(key, file);
  });
  return formData;
}

// POST /api/questions  (Admin only, requires UploadPaper permission) -- step 4 of the wizard.
// `question` is the plain-field object (paperId, questionNumber, subject, ...), `images` is
// { questionImage, optionAImage, optionBImage, optionCImage, optionDImage, explanationImage }.
export function createQuestion(token, question, images = {}) {
  const formData = buildQuestionFormData(
    {
      PaperId: question.paperId,
      QuestionNumber: question.questionNumber,
      Subject: question.subject,
      Topic: question.topic,
      DifficultyLevel: question.difficultyLevel,
      QuestionText: question.questionText,
      OptionA: question.optionA,
      OptionB: question.optionB,
      OptionC: question.optionC,
      OptionD: question.optionD,
      CorrectOption: question.correctOption,
      Explanation: question.explanation,
      SourceReference: question.sourceReference,
      ContentBlocksJson: question.contentBlocksJson,
    },
    {
      QuestionImage: images.questionImage,
      OptionAImage: images.optionAImage,
      OptionBImage: images.optionBImage,
      OptionCImage: images.optionCImage,
      OptionDImage: images.optionDImage,
      ExplanationImage: images.explanationImage,
    }
  );

  return apiFetchForm("/api/questions", { formData, token });
}

// PATCH /api/questions/{id}  (Admin only, requires EditPaper permission)
// `removeImages` is an object of booleans, e.g. { RemoveQuestionImage: true }, for clearing an
// existing image without replacing it.
export function updateQuestion(token, id, question, images = {}, removeImages = {}) {
  const formData = buildQuestionFormData(
    {
      QuestionNumber: question.questionNumber,
      Subject: question.subject,
      Topic: question.topic,
      DifficultyLevel: question.difficultyLevel,
      QuestionText: question.questionText,
      OptionA: question.optionA,
      OptionB: question.optionB,
      OptionC: question.optionC,
      OptionD: question.optionD,
      CorrectOption: question.correctOption,
      Explanation: question.explanation,
      SourceReference: question.sourceReference,
      ContentBlocksJson: question.contentBlocksJson,
      RemoveQuestionImage: removeImages.questionImage ? "true" : "false",
      RemoveOptionAImage: removeImages.optionAImage ? "true" : "false",
      RemoveOptionBImage: removeImages.optionBImage ? "true" : "false",
      RemoveOptionCImage: removeImages.optionCImage ? "true" : "false",
      RemoveOptionDImage: removeImages.optionDImage ? "true" : "false",
      RemoveExplanationImage: removeImages.explanationImage ? "true" : "false",
    },
    {
      QuestionImage: images.questionImage,
      OptionAImage: images.optionAImage,
      OptionBImage: images.optionBImage,
      OptionCImage: images.optionCImage,
      OptionDImage: images.optionDImage,
      ExplanationImage: images.explanationImage,
    }
  );

  return apiFetchForm(`/api/questions/${id}`, { method: "PATCH", formData, token });
}

// DELETE /api/questions/{id}  (Admin only, requires DeletePaper permission)
export function deleteQuestion(token, id) {
  return apiFetch(`/api/questions/${id}`, { method: "DELETE", token });
}
