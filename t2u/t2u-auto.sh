#!/bin/bash
# =====================================================================
# T2U AUTOPILOT — tự động convert Three.js -> Unity, không cần người.
# Cách chạy: mở Terminal, kéo file này vào, bấm Enter.
# Đặt file này trong thư mục dự án web (game Three.js). Skill t2u lấy từ .claude/skills của dự án,
# nếu không có thì từ ~/.claude/skills (cài một lần bằng t2u-kit/install.sh).
# =====================================================================

MODEL_LIGHT="sonnet"   # model cho các bước nhẹ
MODEL_HEAVY="opus"     # model cho bước 3 (chuyển logic) và 6 (đối chiếu) của game cũ (MODE: LEGACY);
                       # game làm sẵn cho Unity (MODE: READY) chạy bước 3/6 bằng MODEL_LIGHT, lượt nào không tiến
                       # triển thì lượt sau đổi sang MODEL_HEAVY (như reference/fast-path.md)
WAIT_MIN=20            # phút chờ khi hết lượt dùng hoặc lỗi
MAX_FAILS=18           # số lần liên tiếp không tiến triển thì dừng hẳn (~6 giờ)

cd "$(dirname "$0")" || exit 1
PROG="unity-convert/PROGRESS.md"
LOGDIR="unity-convert/logs"

say() { echo "[$(date '+%d/%m %H:%M')] $*" | tee -a "$LOGDIR/autopilot.log"; }

mkdir -p "$LOGDIR"

# ---------- Kiểm tra trước khi chạy ----------
ok=1
SKILL=".claude/skills/t2u/SKILL.md"
[ -f "$SKILL" ] || SKILL="$HOME/.claude/skills/t2u/SKILL.md"
[ -f "$SKILL" ] || { say "LỖI: chưa cài skill t2u (chạy t2u-kit/install.sh)."; ok=0; }
command -v claude >/dev/null 2>&1 || { say "LỖI: chưa cài Claude Code (lệnh 'claude')."; ok=0; }
command -v git    >/dev/null 2>&1 || { say "LỖI: chưa có git."; ok=0; }
command -v node   >/dev/null 2>&1 || { say "LỖI: chưa có Node.js."; ok=0; }
[ $ok = 1 ] || exit 1

# Tìm thư mục chứa các bản Unity 6 (máy trong, ổ ngoài, hoặc nơi cài do Unity Hub chỉ định)
UNITY_DIR=""
check_dir() { [ -z "$UNITY_DIR" ] && [ -d "$1" ] && ls "$1" 2>/dev/null | grep -q "^6000" && UNITY_DIR="$1"; }
check_dir "/Applications/Unity/Hub/Editor"
[ -n "$UNITY_EDITORS_DIR" ] && check_dir "$UNITY_EDITORS_DIR"   # tự đặt nếu Unity nằm chỗ khác
for d in /Volumes/*/Unity/Editors /Volumes/*/Unity/Hub/Editor "$HOME/Applications/Unity/Hub/Editor"; do check_dir "$d"; done
HUBCFG="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"
[ -f "$HUBCFG" ] && check_dir "$(tr -d '"\n' < "$HUBCFG")"

if [ -n "$UNITY_DIR" ]; then
  say "Thấy Unity 6 tại: $UNITY_DIR"
else
  # Không hỏi lại để không treo khi vắng người; AI sẽ tự xử lý và ghi vào REVIEW.md
  say "CẢNH BÁO: chưa thấy Unity 6. AI vẫn chạy, nhưng tới bước 2 có thể phải dừng."
fi

# Nếu Unity nằm trên ổ ngoài, nhắc giữ ổ luôn cắm
case "$UNITY_DIR" in /Volumes/*) say "LƯU Ý: Unity nằm trên ổ ngoài — KHÔNG rút ổ trong lúc chạy." ;; esac

say "BẮT ĐẦU AUTOPILOT. Có thể để máy đó. Nhớ cắm sạc và KHÔNG gập nắp MacBook."

# Giữ máy không ngủ trong lúc script chạy
caffeinate -dimsu -w $$ &

PROMPT="AUTOPILOT. Đọc file $SKILL và làm theo ở chế độ Autopilot: chạy bước hiện tại trong unity-convert/PROGRESS.md, không hỏi người dùng, ghi mọi điều cần xem lại vào unity-convert/REVIEW.md. Thư mục chứa các bản Unity Editor trên máy này: ${UNITY_DIR:-chưa tìm thấy}."

fails=0
run=0
while true; do
  if [ -f "$PROG" ] && grep -q "^STATUS: DONE" "$PROG"; then
    say "HOÀN THÀNH! Xem unity-convert/REVIEW.md"; break
  fi
  if [ -f "$PROG" ] && grep -q "^STATUS: STOPPED" "$PROG"; then
    say "AI DỪNG vì cần bạn xử lý. Xem unity-convert/REVIEW.md"; break
  fi

  phase=$(grep -m1 "^CURRENT_PHASE:" "$PROG" 2>/dev/null | grep -o "[0-9]\+" | head -1)
  phase=${phase:-0}
  mode=$(grep -m1 -o "MODE: [A-Z]*" unity-convert/PLAN.md 2>/dev/null | cut -d' ' -f2)
  case "$phase" in
    3|6) if [ "$mode" = READY ] && [ $fails = 0 ]; then model=$MODEL_LIGHT; else model=$MODEL_HEAVY; fi ;;
    *)   model=$MODEL_LIGHT ;;
  esac

  run=$((run+1))
  before=$(cksum "$PROG" 2>/dev/null)
  say "Lượt $run | bước $phase | model $model"

  claude -p "$PROMPT" --model "$model" --dangerously-skip-permissions \
    > "$LOGDIR/run-$run.log" 2>&1
  code=$?

  after=$(cksum "$PROG" 2>/dev/null)
  if [ "$before" = "$after" ]; then
    fails=$((fails+1))
    say "Không có tiến triển (mã $code) — có thể hết lượt dùng. Chờ $WAIT_MIN phút ($fails/$MAX_FAILS)."
    if [ $fails -ge $MAX_FAILS ]; then
      say "DỪNG: quá lâu không tiến triển. Xem log trong $LOGDIR"; break
    fi
    sleep $((WAIT_MIN*60))
  else
    fails=0
  fi
done
