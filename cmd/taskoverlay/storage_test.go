//go:build windows

package main

import "testing"

func TestNormalizeStateAddsPassiveDefaultsToV13State(t *testing.T) {
	st := State{
		NextID: 1,
		Settings: Settings{
			W:         620,
			H:         520,
			FontSize:  20,
			Alpha:     170,
			BgAlpha:   170,
			TextAlpha: 255,
		},
	}

	normalizeState(&st, 13)

	if st.Settings.PassiveMarkerStyle != "dot" {
		t.Fatalf("passive marker = %q, want dot", st.Settings.PassiveMarkerStyle)
	}
	if !st.Settings.ShowCompletedActive {
		t.Fatal("completed tasks should be shown by default after v13 migration")
	}
	if st.Settings.AutoHideDelayMS != 500 {
		t.Fatalf("auto-hide delay = %d, want 500", st.Settings.AutoHideDelayMS)
	}
}

func TestNormalizeStateRoundsAutoHideDelayToSupportedValue(t *testing.T) {
	st := State{
		NextID: 1,
		Settings: Settings{
			W:                   620,
			H:                   520,
			FontSize:            20,
			Alpha:               170,
			BgAlpha:             170,
			TextAlpha:           255,
			AutoHideDelayMS:     1700,
			PassiveMarkerStyle:  "dot",
			ShowCompletedActive: true,
		},
	}

	normalizeState(&st, 15)

	if st.Settings.AutoHideDelayMS != 2000 {
		t.Fatalf("auto-hide delay = %d, want 2000", st.Settings.AutoHideDelayMS)
	}
}

func TestNormalizeStatePreservesSupportedAutoHideDelay(t *testing.T) {
	st := State{
		NextID: 1,
		Settings: Settings{
			W:                  620,
			H:                  520,
			FontSize:           20,
			Alpha:              170,
			BgAlpha:            170,
			TextAlpha:          255,
			AutoHideDelayMS:    3000,
			PassiveMarkerStyle: "dot",
		},
	}

	normalizeState(&st, 14)

	if st.Settings.AutoHideDelayMS != 3000 {
		t.Fatalf("auto-hide delay = %d, want preserved 3000", st.Settings.AutoHideDelayMS)
	}
}

func TestNormalizeStatePreservesV14PassiveSettings(t *testing.T) {
	st := State{
		NextID: 1,
		Settings: Settings{
			W:                   620,
			H:                   520,
			FontSize:            20,
			Alpha:               170,
			BgAlpha:             170,
			TextAlpha:           255,
			PassiveMarkerStyle:  "arrow",
			ShowCompletedActive: false,
		},
	}

	normalizeState(&st, 14)

	if st.Settings.PassiveMarkerStyle != "arrow" {
		t.Fatalf("passive marker = %q, want arrow", st.Settings.PassiveMarkerStyle)
	}
	if st.Settings.ShowCompletedActive {
		t.Fatal("v14 completed-task preference should remain false")
	}
}
