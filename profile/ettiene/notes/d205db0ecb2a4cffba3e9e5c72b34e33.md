# Kibana and Grafana

Where to see what Joktan logs and measures. They run on Dev Tools / Containers: start **joktan-elastic** and **joktan-monitoring**.

## Kibana: logs

[Open the Joktan logs in Kibana](http://localhost:5601/app/discover#/?_a=(dataSource:(dataViewId:joktan-logs,type:dataView)))

- Address: <http://localhost:5601>
- Username and password: none, as security is off.
- Each log has `attributes.app`, which is `joktan`, and `attributes.module`, such as `mqtt`. Filter on them to see one module.
- Choose what is sent on Settings / Logging.

## Grafana: dashboards

[Open the Overview dashboard in Grafana](http://localhost:3000/d/joktan-overview)

- Address: <http://localhost:3000>
- Username: `admin`
- Password: `changeme`
- The dashboard is **Overview**, in the **Joktan** folder.

## The others

| Service | Address | Username and password |
| --- | --- | --- |
| Prometheus | <http://localhost:9090> | None |
| Elasticsearch | <http://localhost:9200> | None |

These only answer on this computer.

## If there is nothing to see

1. Joktan sends to `localhost:4317`, so it has to be running, as does **joktan-monitoring**. That address is set on Settings / Logging, because Rider points what it runs at its own viewer otherwise. Restart Joktan after changing it.
2. After changing their files, use **More**, then **Rebuild and start**, on both projects.
