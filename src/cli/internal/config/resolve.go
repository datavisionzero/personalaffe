package config

import (
	"errors"
	"fmt"
	"strings"

	"github.com/datavisionzero/personalaffe/src/cli/internal/keychain"
)

// Resolved is what a command runs with, and where the credential came from. The
// provenance is not decoration: `pea status` prints it, and "as whom am I about
// to write this" is the question somebody asks a second before they would have
// been sorry.
type Resolved struct {
	Address   string
	Token     string
	TokenFrom string
}

// Input is everything Resolve reads, so that a test supplies all of it and
// nothing reaches around it to the real machine — the keychain included.
type Input struct {
	Getenv         func(string) string
	File           File
	Address        string
	AllowPlainHTTP bool
	Keychain       keychain.Keychain
}

// ResolveAddress answers which instance this invocation talks to: the flag,
// then the environment, then what is on disk.
func (in Input) ResolveAddress() (string, error) {
	address := strings.TrimSpace(in.Address)
	if address == "" {
		address = strings.TrimSpace(in.getenv(EnvURL))
	}
	if address == "" {
		address = strings.TrimSpace(in.File.Instance)
	}
	if address == "" {
		return "", &UsageError{
			Message: fmt.Sprintf(
				"no instance: pass --url https://workspace.example.com, or set %s.", EnvURL),
			Cause: ErrNoInstance,
		}
	}

	address = strings.TrimRight(address, "/")
	if err := CheckAddress(address, in.AllowsPlainHTTP()); err != nil {
		return "", err
	}

	return address, nil
}

// ResolveToken answers the credential and where it came from. The environment
// wins, because that is how an agent receives its own and how CI holds one; a
// file somebody chose is next, because they chose it; and this machine's
// keychain is last, because it is the one rung nobody had to arrange.
//
// The address is what the keychain is keyed by: one machine talks to one
// instance at a time, but nothing says it always talked to this one.
func (in Input) ResolveToken(address string) (string, string, error) {
	if token := strings.TrimSpace(in.getenv(EnvToken)); token != "" {
		return token, EnvToken, nil
	}

	if path := strings.TrimSpace(in.File.TokenFile); path != "" {
		token, err := ReadTokenFile(path)
		if err != nil {
			return "", "", err
		}
		return token, path, nil
	}

	if in.Keychain != nil && address != "" {
		token, err := in.Keychain.Load(address)
		switch {
		case err == nil && strings.TrimSpace(token) != "":
			return strings.TrimSpace(token), Keychain, nil
		case err != nil && !errors.Is(err, keychain.ErrNotFound) && !errors.Is(err, keychain.ErrNoKeychain):
			// The store is there and said something else — a locked keychain,
			// a denied prompt. That is worth saying rather than passing off as
			// "no credential".
			return "", "", &UsageError{Message: fmt.Sprintf("the keychain could not be read: %v", err)}
		}
	}

	return "", "", &UsageError{Message: fmt.Sprintf(
		"no token: put an agent token in %s, or keep one on this machine with `pea login`.", EnvToken)}
}

// Resolve is both at once: what every command that talks to the instance as
// somebody needs.
func Resolve(in Input) (Resolved, error) {
	address, err := in.ResolveAddress()
	if err != nil {
		return Resolved{}, err
	}

	token, from, err := in.ResolveToken(address)
	if err != nil {
		return Resolved{Address: address}, err
	}

	return Resolved{Address: address, Token: token, TokenFrom: from}, nil
}

// AllowsPlainHTTP is the flag or the variable, and nothing inferred.
func (in Input) AllowsPlainHTTP() bool {
	if in.AllowPlainHTTP {
		return true
	}

	switch strings.TrimSpace(in.getenv(EnvInsecureHTTP)) {
	case "1", "true", "yes":
		return true
	default:
		return false
	}
}

func (in Input) getenv(name string) string {
	if in.Getenv == nil {
		return ""
	}
	return in.Getenv(name)
}
