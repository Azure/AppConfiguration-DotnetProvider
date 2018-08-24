{{/*
Create secret for pulling image.
*/}}
{{- define "imagePullSecret" }}
{{- printf "{\"auths\": {\"%s\": {\"auth\": \"%s\"}}}" .Values.imageRegistry (printf "%s:%s" .Values.registryUsername .Values.registryPassword | b64enc) | b64enc }}
{{- end }}
