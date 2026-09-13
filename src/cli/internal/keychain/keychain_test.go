package keychain

import (
	"errors"
	"fmt"
	"strings"
	"testing"
)

// What `security` actually says, in the three shapes that matter. They are
// written down here because the tool's exit code is the signal and its output
// is a remark — a successful delete complains, and a failed lookup exits 44.
const (
	notFoundComplaint = "security: SecKeychainSearchCopyNext: The specified item could not be found " +
		"in the keychain.\nfind-generic-password: returned -25300\n"
	deletedComplaint = "password has been deleted.\n"
	lockedComplaint  = "security: SecKeychainUnlock: User interaction is not allowed.\n"
)

// asking stands in for the tool: what it wrote, what it complained, and whether
// it exited non-zero. No test here touches the keychain of the machine it runs
// on.
func asking(out, complaint string, failed bool) *macOS {
	return &macOS{run: func(string) (string, string, error) {
		if failed {
			return out, complaint, fmt.Errorf("exit status 44")
		}
		return out, complaint, nil
	}}
}

func TestATokenComesBackTrimmed(t *testing.T) {
	token, err := asking("pea_abc\n", "", false).Load("https://workspace.example.com")

	if err != nil {
		t.Fatal(err)
	}
	if token != "pea_abc" {
		t.Fatalf("want the token, got %q", token)
	}
}

func TestNothingStoredIsNotFoundAndNotAFailure(t *testing.T) {
	_, err := asking("", notFoundComplaint, true).Load("https://workspace.example.com")

	if !errors.Is(err, ErrNotFound) {
		t.Fatalf("want ErrNotFound, got %v", err)
	}
}

func TestAKeychainThatSaysSomethingElseSaysItOutLoud(t *testing.T) {
	_, err := asking("", lockedComplaint, true).Load("https://workspace.example.com")

	if errors.Is(err, ErrNotFound) {
		t.Fatal("a locked keychain is not an empty one")
	}
	if !strings.Contains(err.Error(), "User interaction is not allowed") {
		t.Fatalf("the reason was lost: %v", err)
	}
}

// The bug this was written for: a successful delete writes to standard error
// and exits 0, and treating anything on standard error as a failure made every
// `pea logout` fail after doing exactly what was asked.
func TestASuccessfulDeleteComplainsAndIsStillASuccess(t *testing.T) {
	if err := asking("", deletedComplaint, false).Delete("https://workspace.example.com"); err != nil {
		t.Fatalf("a delete that worked was reported as a failure: %v", err)
	}
}

func TestDeletingNothingIsTheStateThatWasAskedFor(t *testing.T) {
	if err := asking("", notFoundComplaint, true).Delete("https://workspace.example.com"); err != nil {
		t.Fatalf("want no error, got %v", err)
	}
}

func TestDeletingAgainstALockedKeychainIsAFailure(t *testing.T) {
	if err := asking("", lockedComplaint, true).Delete("https://workspace.example.com"); err == nil {
		t.Fatal("a locked keychain swallowed a delete")
	}
}

func TestWhatTravelsIntoTheToolIsQuoted(t *testing.T) {
	var sent string

	store := &macOS{run: func(command string) (string, string, error) {
		sent = command
		return "", "", nil
	}}

	if err := store.Save(`https://workspace.example.com/"; rm -rf /`, "pea_abc"); err != nil {
		t.Fatal(err)
	}

	// The values are an address and a generated token, neither of which
	// contains a quote — and the escaping is here so that neither has to be
	// trusted not to.
	if strings.Contains(sent, `/"; rm`) {
		t.Fatalf("a quote travelled unescaped: %q", sent)
	}
	if !strings.Contains(sent, `\"`) {
		t.Fatalf("the quote was not escaped: %q", sent)
	}
}

func TestAMachineWithNoKeychainAnswersRatherThanPretending(t *testing.T) {
	var store Keychain = none{}

	if _, err := store.Load("https://workspace.example.com"); !errors.Is(err, ErrNoKeychain) {
		t.Fatalf("want ErrNoKeychain, got %v", err)
	}
	if err := store.Save("https://workspace.example.com", "pea_abc"); !errors.Is(err, ErrNoKeychain) {
		t.Fatalf("want ErrNoKeychain, got %v", err)
	}
	if store.Where() != "" {
		t.Fatalf("a machine with no keychain named one: %q", store.Where())
	}
}
