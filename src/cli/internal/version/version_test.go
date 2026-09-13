package version_test

import (
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/version"
)

func TestADevelopmentBuildOnEitherSideIsNeverRefused(t *testing.T) {
	for _, pair := range [][2]string{{version.Dev, "1.2.3"}, {"1.2.3", version.Dev}, {"1.2.3", ""}} {
		if ok, _ := version.Compatible(pair[0], pair[1]); !ok {
			t.Errorf("pea %s against instance %q should not be checked", pair[0], pair[1])
		}
	}
}

func TestADifferentMajorIsRefusedInBothDirections(t *testing.T) {
	for _, pair := range [][2]string{{"1.4.0", "2.0.0"}, {"2.0.0", "1.4.0"}} {
		ok, reason := version.Compatible(pair[0], pair[1])
		if ok {
			t.Errorf("pea %s against instance %s should be refused", pair[0], pair[1])
		}
		if !strings.Contains(reason, "major") {
			t.Errorf("the reason does not say why: %s", reason)
		}
	}
}

func TestPeaTalksToItsOwnMinorAndOlderButNotNewer(t *testing.T) {
	if ok, _ := version.Compatible("1.4.0", "1.3.9"); !ok {
		t.Error("an older instance should be fine")
	}
	if ok, _ := version.Compatible("1.4.0", "1.4.7"); !ok {
		t.Error("the same minor should be fine whatever the patch")
	}

	ok, reason := version.Compatible("1.4.0", "1.5.0")
	if ok {
		t.Error("a newer instance should be refused")
	}
	if !strings.Contains(reason, "Upgrade pea") {
		t.Errorf("the reason does not say what to do: %s", reason)
	}
}

func TestAVersionNobodyCanParseIsNotAReasonToRefuse(t *testing.T) {
	if ok, _ := version.Compatible("1.4.0", "the-nightly-one"); !ok {
		t.Error("an unparseable version is not a refusal")
	}
}

func TestAPrereleaseAndItsBuildMetadataAreNotPartOfTheComparison(t *testing.T) {
	if ok, _ := version.Compatible("1.4.0-rc.1+abc123", "1.4.0"); !ok {
		t.Error("a prerelease of the same version should be fine")
	}
}
