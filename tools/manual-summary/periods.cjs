const { requireThat, validateDate, yesterday } = require("./summary-core.cjs");

function shift(date, days) {
  return new Date(Date.parse(date + "T00:00:00Z") + days * 86400000).toISOString().slice(0, 10);
}
function weekEnding(end, now = new Date()) {
  validateDate(end, now);
  requireThat(new Date(end + "T00:00:00Z").getUTCDay() === 0, "주간 종료일은 완료된 일요일이어야 합니다.");
  const start = shift(end, -6);
  return { start, end, key: `weekly:${start}:${end}`, dates: Array.from({ length: 7 }, (_, index) => shift(start, index)) };
}
function latestWeek(now = new Date()) {
  const today = shift(yesterday(now), 1);
  const day = new Date(today + "T00:00:00Z").getUTCDay();
  return weekEnding(shift(today, -(day || 7)), now);
}
function validateSummaryKey(key, now = new Date()) {
  if (!String(key).startsWith("weekly:")) return validateDate(key, now);
  const match = /^weekly:(\d{4}-\d{2}-\d{2}):(\d{4}-\d{2}-\d{2})$/.exec(key);
  requireThat(match && weekEnding(match[2], now).key === key, "완료된 월요일~일요일 주간 키가 아닙니다.");
  return key;
}
function fileStem(key) {
  validateSummaryKey(key);
  return key.replace(/^weekly:/, "weekly-").replaceAll(":", "_");
}
module.exports = { shift, weekEnding, latestWeek, validateSummaryKey, fileStem };
