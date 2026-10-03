FROM ghcr.io/cleanuparr/cleanuparr@sha256:c813a155d0a43b5eb91353f269affe376a211d2ffcb15c1bc3aa76d2d55e5bee
# Keep upstream OS dependencies, Apprise and user-management tools; replace the complete application.
USER root
RUN rm -rf /app && mkdir /app
COPY artifacts/linux-x64/ /app/
COPY repo/code/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh /app/Cleanuparr
ENV PUID=99 PGID=100 TZ=America/New_York PORT=11011
WORKDIR /app
EXPOSE 11011
ENTRYPOINT ["/entrypoint.sh"]
CMD ["./Cleanuparr"]
