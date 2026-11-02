# Copies this profile to Joktan's data folder, which is Joktan in the user's documents.
#
#   make -C profile/ettiene         makes any certificates missing, then copies the profile
#   make -C profile/ettiene certs   only makes the certificates, which are not committed
#
# A file is only overwritten when the one here is newer, so what Joktan has saved since is kept.
# Nothing else in the destination is removed. Saved paths are written here with ~/, which Joktan
# does not expand, so that becomes the user's home folder in the copy.

DEST ?= $(HOME)/Documents/Joktan

.PHONY: copy certs
copy: certs
	mkdir -p "$(DEST)"
	rsync -a --update --exclude .DS_Store --exclude Makefile --exclude make-certs.sh ./ "$(DEST)/"
	find "$(DEST)" -maxdepth 2 -name '*.json' -not -path "$(DEST)/notes/*" -not -path "$(DEST)/messaging/*" -exec perl -pi -e 's#(?<=["\s])~/#$$ENV{HOME}/#g' {} +

certs:
	./make-certs.sh
