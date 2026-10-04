import { apiFetch } from "./client";

// STUDY PARTNER API -- StudyPartnersController (route /api/study-partners). The caller is always the
// logged-in student (JWT); nothing here ever sends "my" user id. Privacy is enforced server-side.
const base = "/api/study-partners";
const j = (method, path, body) => apiFetch(`${base}${path}`, { method, body, auth: true });

export const searchStudyPartners = (q) => j("GET", `/search?q=${encodeURIComponent(q)}`);
export const getRequests = () => j("GET", "/requests");
export const sendRequest = (receiverUserId) => j("POST", "/requests", { receiverUserId });
export const acceptRequest = (id) => j("POST", `/requests/${id}/accept`);
export const rejectRequest = (id) => j("POST", `/requests/${id}/reject`);
export const cancelRequest = (id) => j("DELETE", `/requests/${id}`);

export const getPartners = () => j("GET", "?pageSize=50");
export const getPartnerProfile = (userId) => j("GET", `/${userId}`);
export const getPartnerActivity = (userId) => j("GET", `/${userId}/activity`);
export const comparePartner = (userId, examId) =>
  j("GET", `/${userId}/compare${examId ? `?examId=${examId}` : ""}`);
export const removePartner = (userId) => j("DELETE", `/${userId}`);
export const blockStudent = (userId) => j("POST", `/blocks/${userId}`);

export const getLeaderboard = (period, metric) =>
  j("GET", `/leaderboard?period=${period}&metric=${metric}`);

export const getChallenges = (history = false) => j("GET", `/challenges?scope=${history ? "history" : "active"}`);
export const createChallenge = (body) => j("POST", "/challenges", body);
export const acceptChallenge = (id) => j("POST", `/challenges/${id}/accept`);
export const rejectChallenge = (id) => j("POST", `/challenges/${id}/reject`);
export const cancelChallenge = (id) => j("DELETE", `/challenges/${id}`);
export const completeChallenge = (id) => j("POST", `/challenges/${id}/complete`);

export const getPrivacy = () => j("GET", "/privacy");
export const savePrivacy = (body) => j("PUT", "/privacy", body);
