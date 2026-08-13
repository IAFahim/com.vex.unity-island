OUT := ../Assets/Plugins/Linux/x86_64/libisland.so

$(OUT): island_x11.c
	mkdir -p $(dir $(OUT))
	gcc -shared -fPIC -O2 -s -o $(OUT) island_x11.c -lX11 -l:libXext.so.6 -l:libXinerama.so.1 -l:libXfixes.so.3 -l:libXi.so.6 -lpthread

.PHONY: clean
clean:
	rm -f $(OUT)
