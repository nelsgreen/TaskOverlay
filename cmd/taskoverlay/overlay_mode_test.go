//go:build windows

package main

import (
	"strings"
	"testing"
)

func TestPassiveBoundsShrinkToVisibleTasks(t *testing.T) {
	a := App{
		state: State{
			Settings: Settings{
				X:        100,
				Y:        200,
				W:        620,
				H:        520,
				FontSize: 20,
			},
			Tasks: []Task{
				{ID: 1, Text: "Short active task", Expanded: true},
				{ID: 2, Text: "Completed task", Done: true},
			},
		},
	}

	bounds := a.passiveBounds()

	if bounds.Left != 100 || bounds.Top != 200 {
		t.Fatalf("passive position = %d,%d, want 100,200", bounds.Left, bounds.Top)
	}
	if width := bounds.Right - bounds.Left; width >= a.state.Settings.W {
		t.Fatalf("passive width = %d, want less than active width %d", width, a.state.Settings.W)
	}
	if height := bounds.Bottom - bounds.Top; height >= a.state.Settings.H {
		t.Fatalf("passive height = %d, want less than active height %d", height, a.state.Settings.H)
	}
}

func TestPassiveBoundsNeverExceedActiveBounds(t *testing.T) {
	a := App{
		state: State{
			Settings: Settings{
				X:        10,
				Y:        20,
				W:        420,
				H:        320,
				FontSize: 24,
			},
			Tasks: []Task{
				{ID: 1, Text: strings.Repeat("very long task ", 30), Expanded: true},
			},
		},
	}

	bounds := a.passiveBounds()

	if width := bounds.Right - bounds.Left; width > a.state.Settings.W {
		t.Fatalf("passive width = %d, exceeds active width %d", width, a.state.Settings.W)
	}
	if height := bounds.Bottom - bounds.Top; height > a.state.Settings.H {
		t.Fatalf("passive height = %d, exceeds active height %d", height, a.state.Settings.H)
	}
}

func TestHasBlinkingTasksIgnoresCompletedTasks(t *testing.T) {
	a := App{
		state: State{
			Tasks: []Task{
				{ID: 1, Blink: true, Done: true},
				{ID: 2, Blink: false},
			},
		},
	}

	if a.hasBlinkingTasks() {
		t.Fatal("completed blinking task should not trigger repaint")
	}

	a.state.Tasks[1].Blink = true
	if !a.hasBlinkingTasks() {
		t.Fatal("active blinking task should trigger repaint")
	}
}
