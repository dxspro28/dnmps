build:
	@echo Building...
	@mcs src/Program.cs -out:bin/dnmps -unsafe

install:
	@echo Installing...
	@cp bin/dnmps ~/.local/bin/
