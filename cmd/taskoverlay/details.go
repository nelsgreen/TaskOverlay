//go:build windows

package main

func (a *App) drawTaskDetails(hdc HDC, rc RECT, top int32, task Task) int32 {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	h := a.scale(86)
	panel := RECT{Left: a.scale(34), Top: top, Right: rc.Right - a.scale(10), Bottom: top + h}
	if s.ShowBorders {
		drawBorder(hdc, panel, s.BorderColor)
	}
	x := panel.Left + a.scale(8)
	y := panel.Top + a.scale(6)
	drawText(hdc, "Описание", RECT{Left: x, Top: y, Right: panel.Right - a.scale(8), Bottom: y + a.scale(22)}, tc, a.font(max(13, s.FontSize-5)), true, s.Shadow, s.Outline, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	y += a.scale(24)
	descR := RECT{Left: x, Top: y, Right: panel.Right - a.scale(8), Bottom: panel.Bottom - a.scale(8)}
	a.addAction(descR, "edit_description", task.ID, 0)
	desc := task.Description
	if desc == "" {
		desc = "Кликните, чтобы добавить описание"
	}
	if a.editActive && a.editTaskID == task.ID && a.editKind == "description" {
		desc = a.editText
		if a.blinkOn {
			desc += "|"
		}
		drawBorder(hdc, descR, tc)
	}
	drawText(hdc, desc, descR, tc, a.font(max(13, s.FontSize-5)), false, false, false, DT_LEFT|DT_WORDBREAK|DT_END_ELLIPSIS|DT_NOPREFIX)
	return h
}

func minInt(a, b int) int {
	if a < b {
		return a
	}
	return b
}
