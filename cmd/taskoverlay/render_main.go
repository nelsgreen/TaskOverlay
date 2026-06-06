//go:build windows

package main

import (
	"strconv"
	"unsafe"
)

func (a *App) paint() {
	var ps PAINTSTRUCT
	hdc, _, _ := procBeginPaint.Call(uintptr(a.hwnd), uintptr(unsafe.Pointer(&ps)))
	var rc RECT
	procGetClientRect.Call(uintptr(a.hwnd), uintptr(unsafe.Pointer(&rc)))

	fillRect(HDC(hdc), rc, a.state.Settings.BgColor)
	a.actions = nil

	a.drawHeader(HDC(hdc), rc)
	top := a.scale(38)
	if a.settingsOpen {
		if !a.settingsPaintLogged {
			logf("paint settings begin rc=%d,%d,%d,%d", rc.Left, rc.Top, rc.Right, rc.Bottom)
		}
		top = a.drawSettings(HDC(hdc), rc, top)
		if !a.settingsPaintLogged {
			logf("paint settings end top=%d", top)
			a.settingsPaintLogged = true
		}
	}
	a.drawTasks(HDC(hdc), rc, top)
	if a.status != "" {
		r := RECT{Left: a.scale(12), Top: rc.Bottom - a.scale(26), Right: rc.Right - a.scale(12), Bottom: rc.Bottom - a.scale(4)}
		drawText(HDC(hdc), a.status, r, a.effectiveTextColor(), a.font(max(14, a.state.Settings.FontSize-3)), false, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX)
	}
	a.drawResizeGrip(HDC(hdc), rc)
	procEndPaint.Call(uintptr(a.hwnd), uintptr(unsafe.Pointer(&ps)))
}

func (a *App) drawHeader(hdc HDC, rc RECT) {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	m := a.scale(12)
	title := RECT{Left: m, Top: a.scale(7), Right: rc.Right - a.scale(110), Bottom: a.scale(32)}
	drawText(hdc, "TaskOverlay", title, tc, a.font(max(15, s.FontSize-4)), true, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	btnW := a.scale(26)
	gap := a.scale(6)
	btnTop := a.scale(5)
	btnBot := a.scale(31)
	closeR := RECT{Left: rc.Right - m - btnW, Top: btnTop, Right: rc.Right - m, Bottom: btnBot}
	addR := RECT{Left: closeR.Left - gap - btnW, Top: btnTop, Right: closeR.Left - gap, Bottom: btnBot}
	setR := RECT{Left: addR.Left - gap - btnW, Top: btnTop, Right: addR.Left - gap, Bottom: btnBot}
	a.addAction(setR, "settings", 0, 0)
	a.addAction(addR, "add", 0, 0)
	a.addAction(closeR, "close", 0, 0)
	drawButton(hdc, "⚙", setR, tc, a.font(s.FontSize))
	drawButton(hdc, "+", addR, tc, a.font(s.FontSize+2))
	drawButton(hdc, "x", closeR, tc, a.font(s.FontSize))
}

func (a *App) drawSettings(hdc HDC, rc RECT, top int32) int32 {
	s := &a.state.Settings
	tc := a.effectiveTextColor()
	panelH := a.scale(245)
	if a.dropdown == "bg" {
		panelH += a.scale(int32(len(bgOptions())) * 24)
	}
	if a.dropdown == "text" {
		panelH += a.scale(int32(len(textOptions())) * 24)
	}
	panel := RECT{Left: a.scale(8), Top: top, Right: rc.Right - a.scale(8), Bottom: top + panelH}
	if s.ShowBorders {
		drawBorder(hdc, panel, s.BorderColor)
	}
	y := top + a.scale(8)
	drawTextSafe(hdc, "Настройки", RECT{Left: a.scale(16), Top: y, Right: rc.Right - a.scale(16), Bottom: y + a.scale(22)}, tc, a.font(s.FontSize), true, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	y += a.scale(28)
	y = a.dropdownLine(hdc, y, "Фон", colorName(bgOptions(), s.BgColor), "drop_bg")
	if a.dropdown == "bg" {
		y = a.drawColorOptions(hdc, y, "bg", bgOptions(), s.BgColor)
	}
	y = a.dropdownLine(hdc, y, "Текст", colorName(textOptions(), s.TextColor), "drop_text")
	if a.dropdown == "text" {
		y = a.drawColorOptions(hdc, y, "text", textOptions(), s.TextColor)
	}
	y += a.scale(2)
	a.settingsLineSafe(hdc, y, "Фон прозрачн.", []string{"-", strconv.Itoa(int(s.BgAlpha)), "+"}, []string{"bg_alpha_minus", "noop", "bg_alpha_plus"})
	y += a.scale(26)
	a.settingsLineSafe(hdc, y, "Текст прозрачн.", []string{"-", strconv.Itoa(int(s.TextAlpha)), "+"}, []string{"text_alpha_minus", "noop", "text_alpha_plus"})
	y += a.scale(26)
	a.settingsLineSafe(hdc, y, "Шрифт", []string{"-", strconv.Itoa(int(s.FontSize)), "+", "B", "тень", "объем"}, []string{"font_minus", "noop", "font_plus", "toggle_bold", "toggle_shadow", "toggle_outline"})
	y += a.scale(26)
	a.settingsLineSafe(hdc, y, "Выполненные", []string{"развернуть", "стиль", "рамка"}, []string{"toggle_completed", "toggle_done_style", "toggle_border"})
	y += a.scale(26)
	a.settingsLineSafe(hdc, y, "Экспорт", []string{"сегодня", "7 дней", "30 дней", "все"}, []string{"export_1", "export_7", "export_30", "export_all"})
	y += a.scale(26)
	a.settingsLineSafe(hdc, y, "Диагностика", []string{"выгрузить", "логи", "сброс окна"}, []string{"diag_export", "open_logs", "reset_window"})
	return panel.Bottom + a.scale(8)
}

func (a *App) dropdownLine(hdc HDC, y int32, label, value, kind string) int32 {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	drawTextSafe(hdc, label+":", RECT{Left: a.scale(18), Top: y, Right: a.scale(135), Bottom: y + a.scale(22)}, tc, a.font(max(14, s.FontSize-3)), false, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	r := RECT{Left: a.scale(140), Top: y, Right: a.scale(300), Bottom: y + a.scale(22)}
	a.addAction(r, kind, 0, 0)
	drawBorder(hdc, r, s.BorderColor)
	drawButtonSafe(hdc, value+" ▼", r, tc, a.font(max(14, s.FontSize-3)))
	return y + a.scale(26)
}

func (a *App) drawColorOptions(hdc HDC, y int32, group string, opts []ColorOption, selected uint32) int32 {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	for i, opt := range opts {
		r := RECT{Left: a.scale(140), Top: y, Right: a.scale(330), Bottom: y + a.scale(22)}
		kind := "set_" + group + "_" + strconv.Itoa(i)
		a.addAction(r, kind, 0, 0)
		fillRect(hdc, RECT{Left: r.Left + a.scale(4), Top: r.Top + a.scale(4), Right: r.Left + a.scale(20), Bottom: r.Bottom - a.scale(4)}, opt.Color)
		if opt.Color == selected {
			drawBorder(hdc, r, tc)
		}
		drawTextSafe(hdc, opt.Name, RECT{Left: r.Left + a.scale(26), Top: r.Top, Right: r.Right - a.scale(4), Bottom: r.Bottom}, tc, a.font(max(14, s.FontSize-3)), false, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
		y += a.scale(24)
	}
	return y + a.scale(2)
}

func (a *App) settingsLine(hdc HDC, y int32, label string, texts []string, kinds []string) {
	s := a.state.Settings
	drawText(hdc, label+":", RECT{Left: 18, Top: y, Right: 135, Bottom: y + 22}, s.TextColor, max(14, s.FontSize-3), false, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	x := int32(140)
	for i, t := range texts {
		w := int32(20 + len([]rune(t))*9)
		if w < 32 {
			w = 32
		}
		r := RECT{Left: x, Top: y, Right: x + w, Bottom: y + 22}
		a.addAction(r, kinds[i], 0, 0)
		drawButton(hdc, t, r, s.TextColor, max(14, s.FontSize-3))
		x += w + 8
	}
}

func (a *App) settingsLineSafe(hdc HDC, y int32, label string, texts []string, kinds []string) {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	drawTextSafe(hdc, label+":", RECT{Left: a.scale(18), Top: y, Right: a.scale(135), Bottom: y + a.scale(22)}, tc, a.font(max(14, s.FontSize-3)), false, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	x := a.scale(140)
	for i, t := range texts {
		if i >= len(kinds) {
			break
		}
		w := a.scale(22 + int32(len([]rune(t))*9))
		if w < a.scale(32) {
			w = a.scale(32)
		}
		if x+w > 3000 {
			break
		}
		r := RECT{Left: x, Top: y, Right: x + w, Bottom: y + a.scale(22)}
		a.addAction(r, kinds[i], 0, 0)
		drawButtonSafe(hdc, t, r, tc, a.font(max(14, s.FontSize-3)))
		x += w + a.scale(8)
	}
}

func (a *App) drawTasks(hdc HDC, rc RECT, top int32) {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	y := top - a.scroll
	drawRow := func(task Task, y int32) int32 {
		indent := int32(0)
		if task.ParentID != 0 {
			indent = a.scale(24)
		}
		rowH := a.scale(max(30, s.FontSize+12))
		if y+rowH < top {
			return y + rowH + a.scale(6)
		}
		if y > rc.Bottom-a.scale(32) {
			return y + rowH + a.scale(6)
		}

		row := RECT{Left: a.scale(8) + indent, Top: y, Right: rc.Right - a.scale(8), Bottom: y + rowH}
		if task.Blink && a.blinkOn {
			fillRect(hdc, row, adjustColor(s.BgColor, 55))
		}
		if task.InWork {
			drawBorder(hdc, row, tc)
		} else if s.ShowBorders {
			drawBorder(hdc, row, s.BorderColor)
		}
		x := row.Left + a.scale(6)
		cb := RECT{Left: x, Top: y + a.scale(7), Right: x + a.scale(16), Bottom: y + a.scale(23)}
		a.addAction(cb, "toggle_done", task.ID, 0)
		drawBorder(hdc, cb, tc)
		if task.Done {
			drawText(hdc, "✓", RECT{Left: cb.Left - a.scale(1), Top: cb.Top - a.scale(3), Right: cb.Right + a.scale(5), Bottom: cb.Bottom + a.scale(3)}, tc, a.font(s.FontSize), true, false, false, DT_CENTER|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
		}
		x += a.scale(22)

		if a.hasChildren(task.ID) {
			ex := RECT{Left: x, Top: y + a.scale(5), Right: x + a.scale(18), Bottom: y + a.scale(25)}
			a.addAction(ex, "expand", task.ID, 0)
			sym := "▸"
			if task.Expanded {
				sym = "▾"
			}
			drawButton(hdc, sym, ex, tc, a.font(s.FontSize))
		}
		x += a.scale(20)

		pr := priorityLabel(task.Priority)
		prR := RECT{Left: x, Top: y + a.scale(5), Right: x + a.scale(34), Bottom: y + a.scale(25)}
		a.addAction(prR, "priority", task.ID, 0)
		drawButton(hdc, pr, prR, tc, a.font(max(13, s.FontSize-4)))
		x += a.scale(38)

		textRight := rc.Right - a.scale(170)
		txt := RECT{Left: x, Top: y + a.scale(4), Right: textRight, Bottom: y + rowH - a.scale(4)}
		a.addAction(txt, "edit_text", task.ID, 0)
		color := tc
		if task.Done && s.DoneStyle == 1 {
			color = s.BorderColor
		}
		textToDraw := task.Text
		if a.editActive && a.editTaskID == task.ID && a.editKind == "text" {
			textToDraw = a.editText
			if a.blinkOn {
				textToDraw += "|"
			}
			drawBorder(hdc, txt, color)
		}
		drawText(hdc, textToDraw, txt, color, a.font(s.FontSize), s.Bold, s.Shadow, s.Outline, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX)
		if task.Done {
			drawStrike(hdc, txt, color)
		}

		bx := rc.Right - a.scale(162)
		buttons := []struct {
			txt, kind string
			w         int32
		}{
			{"+", "add_child", 22},
			{"i", "details", 22},
			{"⏰", "edit_due", 34},
			{"▶", "inwork", 24},
			{"×", "delete", 24},
		}
		for _, b := range buttons {
			bw := a.scale(b.w)
			r := RECT{Left: bx, Top: y + a.scale(5), Right: bx + bw, Bottom: y + a.scale(25)}
			a.addAction(r, b.kind, task.ID, 0)
			drawButton(hdc, b.txt, r, tc, a.font(max(14, s.FontSize-3)))
			bx += bw + a.scale(4)
		}
		due := task.DueHHMM
		if a.editActive && a.editTaskID == task.ID && a.editKind == "due" {
			due = a.editText
			if a.blinkOn {
				due += "|"
			}
		}
		if due == "" {
			due = "--:--"
		}
		dueR := RECT{Left: rc.Right - a.scale(70), Top: y + a.scale(5), Right: rc.Right - a.scale(10), Bottom: y + a.scale(25)}
		a.addAction(dueR, "edit_due", task.ID, 0)
		if a.editActive && a.editTaskID == task.ID && a.editKind == "due" {
			drawBorder(hdc, dueR, tc)
		}
		drawButton(hdc, due, dueR, tc, a.font(max(13, s.FontSize-4)))
		nextY := y + rowH + a.scale(6)
		if a.detailsTaskID == task.ID {
			detailsH := a.drawTaskDetails(hdc, rc, nextY, task)
			nextY += detailsH + a.scale(6)
		}
		return nextY
	}

	activeTasks := a.visibleTasks(false)
	for _, task := range activeTasks {
		if y > rc.Bottom-a.scale(32) {
			break
		}
		y = drawRow(task, y)
	}

	doneTasks := a.visibleTasks(true)
	if len(doneTasks) > 0 {
		headerH := a.scale(28)
		if y+headerH >= top && y <= rc.Bottom-a.scale(32) {
			head := RECT{Left: a.scale(8), Top: y, Right: rc.Right - a.scale(8), Bottom: y + headerH}
			a.addAction(head, "toggle_completed", 0, 0)
			label := "Завершенные: " + strconv.Itoa(len(doneTasks))
			if s.CompletedExpanded {
				label += "  ▾"
			} else {
				label += "  ▸"
			}
			drawText(hdc, label, head, tc, a.font(max(14, s.FontSize-4)), true, s.Shadow, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
		}
		y += headerH + a.scale(4)
		if s.CompletedExpanded {
			for _, task := range doneTasks {
				if y > rc.Bottom-a.scale(32) {
					break
				}
				y = drawRow(task, y)
			}
		}
	}
}

func (a *App) visibleTasks(done bool) []Task {
	var result []Task
	parents := make([]Task, 0)
	for _, t := range a.state.Tasks {
		if t.ParentID == 0 && t.Done == done {
			parents = append(parents, t)
		}
	}
	for _, p := range parents {
		result = append(result, p)
		if p.Expanded {
			for _, c := range a.state.Tasks {
				if c.ParentID == p.ID && c.Done == done {
					result = append(result, c)
				}
			}
		}
	}
	for _, c := range a.state.Tasks {
		if c.ParentID != 0 && c.Done == done && !a.hasParentWithDoneState(c.ParentID, done) {
			result = append(result, c)
		}
	}
	return result
}

func (a *App) hasParentWithDoneState(parentID int64, done bool) bool {
	for _, t := range a.state.Tasks {
		if t.ID == parentID {
			return t.Done == done
		}
	}
	return false
}
