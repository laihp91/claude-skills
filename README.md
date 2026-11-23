# Claude Code skills

Bộ skill dùng chung cho Claude Code. Thư mục này chính là `~/.claude/skills`
trên máy, nên mọi skill ở đây có mặt trong mọi dự án.

## Cài lên máy mới

```bash
# Nếu máy đã có ~/.claude/skills thì cất đi trước
[ -e ~/.claude/skills ] && mv ~/.claude/skills ~/.claude/skills.old
gh auth login --web    # lần đầu trên máy mới; cài gh bằng: brew install gh
gh auth setup-git
git clone https://github.com/laihp91/claude-skills.git ~/.claude/skills
```

Mở phiên Claude Code mới là thấy skill.

## Cập nhật

Sửa skill ở máy nào thì đẩy lên từ máy đó:

```bash
cd ~/.claude/skills && git add -A && git commit -m "Update t2u" && git push
```

Lấy bản mới ở máy khác:

```bash
cd ~/.claude/skills && git pull
```

## Phụ thuộc không nằm trong repo

- `t2u`: cần ổ TRANSCEND, vì file `t2u/KIT_SOURCE` trỏ tới
  `/Volumes/TRANSCEND/Project_Unity/t2u-kit`, và cần Unity đã cài.
- `graphify`: cần cài công cụ dòng lệnh `graphify` riêng.
- `unity-mcp-skill`: cần Unity MCP đã kết nối trong Unity Editor.
