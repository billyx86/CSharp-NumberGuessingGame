# Makefile — local build + test entry point for Linux/macOS (issue #13).
#
# Mirrors .github/workflows/ci.yml so a Unix contributor can run the same
# pipeline locally instead of only in CI:
#
#   make build   # compile game + tests with mcs (Mono)
#   make test    # compile, run the unit suite, run all smoke tests
#   make clean   # remove build output
#
# Requires Mono (mcs + mono):  sudo apt-get install -y mono-complete
# Windows contributors: use .\run-tests.ps1 (MSBuild) instead.

SHELL := /bin/sh

GAME_SOURCES := BasicGuessingGame/Program.cs BasicGuessingGame/GameEngine.cs
TEST_SOURCES := BasicGuessingGame.Tests/GameEngineTests.cs BasicGuessingGame/GameEngine.cs

.PHONY: build test clean

build:
	mkdir -p BasicGuessingGame/bin
	mcs -warn:4 -out:BasicGuessingGame/bin/Guess.exe $(GAME_SOURCES)
	mkdir -p BasicGuessingGame.Tests/bin
	mcs -warn:4 -out:BasicGuessingGame.Tests/bin/GameEngineTests.exe $(TEST_SOURCES)

test:
	sh run-tests.sh

clean:
	rm -rf BasicGuessingGame/bin BasicGuessingGame.Tests/bin
